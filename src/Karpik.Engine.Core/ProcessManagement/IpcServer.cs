using System.IO.Pipes;

using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core;

public class IpcServer : IDisposable
{
    private static readonly AsyncLocal<bool> s_inListener = new();
    private NamedPipeServerStream? _pipe;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _stateRequestGate = new(1, 1);
    private readonly SemaphoreSlim _editorSnapshotGate = new(1, 1);
    private readonly SemaphoreSlim _shutdownRequestGate = new(1, 1);
    private int _disposeRequested;
    private Task? _listenTask;
    private readonly ILogger<IpcServer> _logger;
    private readonly ILoggerFactory? _ownedLoggerFactory;
    private ModLiveLaunchAssociation? _modLiveAssociation;
    private int _modLiveConnectAttempted;
    private ModLiveIpcPeer? _modLivePeer;

    internal ModLiveIpcPeer? ModLivePeer => Volatile.Read(ref _modLivePeer);

    internal void ConfigureModLive(ModLiveLaunchAssociation association)
    {
        if (_pipe is not null || _modLiveAssociation is not null)
            throw new InvalidOperationException("Mod-live IPC must be configured before connecting.");
        _modLiveAssociation = association;
    }
    
    public event Action<IpcMessage>? OnMessageReceived;
    public bool IsConnected => _pipe?.IsConnected ?? false;
    internal static bool IsInListenerContext => s_inListener.Value;
    
    public IpcServer(string pipeName)
        : this(pipeName, HostLogging.CreateDefaultFactory(), ownsFactory: true)
    {
    }

    public IpcServer(string pipeName, ILoggerFactory loggerFactory)
        : this(pipeName, loggerFactory, ownsFactory: false)
    {
    }

    private IpcServer(string pipeName, ILoggerFactory loggerFactory, bool ownsFactory)
    {
        _pipeName = pipeName;
        _logger = loggerFactory.CreateLogger<IpcServer>();
        _ownedLoggerFactory = ownsFactory ? loggerFactory : null;
    }
    
    public async Task WaitForConnectionAsync(CancellationToken cancellationToken = default)
    {
        var association = _modLiveAssociation;
        if (association is not null)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            if (Interlocked.Exchange(ref _modLiveConnectAttempted, 1) != 0)
                throw new InvalidOperationException("An associated IPC connection cannot be reused.");
        }
        var pipe = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: association is null ? NamedPipeServerStream.MaxAllowedServerInstances : 1,
            PipeTransmissionMode.Byte,
            association is null ? PipeOptions.Asynchronous : PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _pipe = pipe;

        _logger.LogInformation("Waiting for worker connection on pipe {PipeName}", _pipeName);
        try
        {
            await pipe.WaitForConnectionAsync(cancellationToken);
            if (association is not null)
            {
                await association.AuthenticateAsync(pipe, isOwner: true, SendFrameAsync, cancellationToken);
                Volatile.Write(ref _modLivePeer, new ModLiveIpcPeer(
                    association.Identity, isOwner: true, SendFrameAsync, pipe.Dispose));
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            }
            _logger.LogInformation("Worker connected");
            _listenTask = ListenLoop(pipe, _cts.Token);
        }
        catch
        {
            if (association is not null)
            {
                ModLivePeer?.Close();
                association.Dispose();
                pipe.Dispose();
            }
            throw;
        }
    }

    public Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (ModLiveIpcProtocol.IsReserved(message.Type))
            throw new ArgumentException("Reserved IPC frame type.", nameof(message));
        return SendFrameAsync(message, cancellationToken);
    }

    private async Task SendFrameAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            if (_pipe == null || !_pipe.IsConnected ||
                (message.Type == ModLiveIpcProtocol.Control && ModLivePeer?.Loss.IsCompleted != false))
                throw new InvalidOperationException("Pipe is not connected");

            var bytes = message.ToBytes();
            await _pipe.WriteAsync(bytes, cancellationToken);
            await _pipe.FlushAsync(cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }
    
    public async Task<HotReloadState?> RequestStateAsync(CancellationToken cancellationToken = default)
    {
        var (_, state) = await TryRequestStateAsync(TimeSpan.FromSeconds(10), cancellationToken);
        return state;
    }

    public async Task<(bool Received, HotReloadState? State)> TryRequestStateAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        await _stateRequestGate.WaitAsync(cancellationToken);
        var tcs = new TaskCompletionSource<HotReloadState?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(IpcMessage msg)
        {
            if (msg.Type == IpcMessageType.StateResponse)
            {
                if (msg.Payload.Length > 0)
                {
                    var state = HotReloadState.Deserialize(msg.Payload);
                    tcs.TrySetResult(state);
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            }
        }

        OnMessageReceived += Handler;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SendAsync(new IpcMessage(IpcMessageType.StateRequest), CancellationToken.None);
            }
            catch (Exception exception) when (
                Volatile.Read(ref _disposeRequested) != 0
                && exception is IOException or ObjectDisposedException or InvalidOperationException)
            {
                return (false, null);
            }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            cts.CancelAfter(timeout);
            try
            {
                return (true, await tcs.Task.WaitAsync(cts.Token));
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Timeout waiting for StateResponse");
                return (false, null);
            }
        }
        finally
        {
            OnMessageReceived -= Handler;
            _stateRequestGate.Release();
        }
    }

    public async Task<EditorRuntimeSnapshot?> RequestEditorSnapshotAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        await _editorSnapshotGate.WaitAsync(cancellationToken);
        var tcs = new TaskCompletionSource<EditorRuntimeSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(IpcMessage message)
        {
            if (message.Type != IpcMessageType.EditorSnapshotResponse)
            {
                return;
            }

            OnMessageReceived -= Handler;
            tcs.TrySetResult(
                message.Payload.Length == 0
                    ? null
                    : EditorRuntimeSnapshot.Deserialize(message.Payload));
        }

        OnMessageReceived += Handler;
        try
        {
            await SendAsync(new IpcMessage(IpcMessageType.EditorSnapshotRequest), cancellationToken);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            cts.CancelAfter(timeout);
            return await tcs.Task.WaitAsync(cts.Token);
        }
        finally
        {
            OnMessageReceived -= Handler;
            _editorSnapshotGate.Release();
        }
    }
    
    public Task SendShutdownRequestAsync(CancellationToken cancellationToken = default)
    {
        return SendShutdownRequestAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    public async Task SendShutdownRequestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await _shutdownRequestGate.WaitAsync(cancellationToken);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        void Handler(IpcMessage msg)
        {
            if (msg.Type == IpcMessageType.ShutdownAck)
            {
                tcs.TrySetResult(true);
            }
        }

        OnMessageReceived += Handler;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SendAsync(new IpcMessage(IpcMessageType.ShutdownRequest), CancellationToken.None);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            cts.CancelAfter(timeout);
            try
            {
                await tcs.Task.WaitAsync(cts.Token);
                _logger.LogInformation("Worker acknowledged shutdown");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Timeout waiting for ShutdownAck; will force kill");
            }
        }
        finally
        {
            OnMessageReceived -= Handler;
            _shutdownRequestGate.Release();
        }
    }
    
    private async Task ListenLoop(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        s_inListener.Value = true;
        var headerBuffer = new byte[5];
        var live = ModLivePeer;
        try
        {
            while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
            {
                IpcMessage? received = await IpcFrame.ReadAsync(pipe, headerBuffer, cancellationToken);
                if (received is not { } message)
                {
                    _logger.LogInformation("Worker disconnected");
                    break;
                }
                if (ModLiveIpcProtocol.IsReserved(message.Type))
                {
                    if (live is not null && !live.Loss.IsCompleted && !live.TryReceive(message)) break;
                    continue;
                }
                OnMessageReceived?.Invoke(message);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Pipe error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Listen error");
        }
        finally
        {
            if (live is not null)
            {
                live.Close();
                pipe.Dispose();
            }
            s_inListener.Value = false;
        }
    }
    
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        ModLivePeer?.Close();
        _cts.Cancel();

        try
        {
            _pipe?.Dispose();
        }
        catch
        {
            // Ignore pipe disposal errors during worker restart/shutdown.
        }

        if (_listenTask is { IsCompleted: false } listenTask)
        {
            _ = listenTask.ContinueWith(
                static (_, state) => ((IpcServer)state!).DisposeListenerResources(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return;
        }

        DisposeListenerResources();
    }

    internal async Task WaitForListenerAsync()
    {
        if (_listenTask is { } listenTask)
        {
            try
            {
                await listenTask.ConfigureAwait(false);
            }
            catch
            {
                // Listener failures were handled at the receive boundary.
            }
        }
    }

    private void DisposeListenerResources()
    {
        _cts.Dispose();
        _ownedLoggerFactory?.Dispose();
    }
}
