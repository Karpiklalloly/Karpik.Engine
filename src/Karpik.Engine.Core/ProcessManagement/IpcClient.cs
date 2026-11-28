using System.IO.Pipes;

using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core;

internal class IpcClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private Task? _listenTask;
    private MainThreadScheduler? _scheduler;
    private readonly ILogger<IpcClient> _logger;
    private readonly ILoggerFactory? _ownedLoggerFactory;
    private int _disposeRequested;
    private ModLiveLaunchAssociation? _modLiveAssociation;
    private int _modLiveConnectAttempted;
    private ModLiveIpcPeer? _modLivePeer;
    private int _modLiveCapturePending;

    internal ModLiveIpcPeer? ModLivePeer => Volatile.Read(ref _modLivePeer);
    // The host owns admission and scheduling. In maintenance return null without posting to the leased queue.
    internal Func<CancellationToken, Task<HotReloadState?>>? OnModLiveStateRequest { get; set; }
    internal Func<CancellationToken, Task<EditorRuntimeSnapshot?>>? OnModLiveEditorSnapshotRequest { get; set; }

    internal void ConfigureModLive(ModLiveLaunchAssociation association)
    {
        if (_pipe is not null || _modLiveAssociation is not null)
            throw new InvalidOperationException("Mod-live IPC must be configured before connecting.");
        _modLiveAssociation = association;
    }
    
    public event Action<IpcMessage>? OnMessageReceived;
    public bool IsConnected => _pipe?.IsConnected ?? false;
    
    public Func<HotReloadState?>? OnStateRequest { get; set; }
    public Func<EditorRuntimeSnapshot?>? OnEditorSnapshotRequest { get; set; }
    public Action? OnShutdownRequest { get; set; }
    
    public IpcClient(string pipeName)
        : this(pipeName, HostLogging.CreateDefaultFactory(), ownsFactory: true)
    {
    }

    public IpcClient(string pipeName, ILoggerFactory loggerFactory)
        : this(pipeName, loggerFactory, ownsFactory: false)
    {
    }

    private IpcClient(string pipeName, ILoggerFactory loggerFactory, bool ownsFactory)
    {
        _pipeName = pipeName;
        _logger = loggerFactory.CreateLogger<IpcClient>();
        _ownedLoggerFactory = ownsFactory ? loggerFactory : null;
    }
    
    public void SetScheduler(MainThreadScheduler scheduler)
    {
        _scheduler = scheduler;
    }
    
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var association = _modLiveAssociation;
        if (association is not null)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            if (Interlocked.Exchange(ref _modLiveConnectAttempted, 1) != 0)
                throw new InvalidOperationException("An associated IPC connection cannot be reused.");
        }
        var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut,
            association is null ? PipeOptions.Asynchronous : PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _pipe = pipe;

        _logger.LogInformation("Connecting to watcher on pipe {PipeName}", _pipeName);
        if (association is null)
        {
            await pipe.ConnectAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        else
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            connect.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await pipe.ConnectAsync(connect.Token);
                await association.AuthenticateAsync(pipe, isOwner: false, SendFrameAsync, connect.Token);
                Volatile.Write(ref _modLivePeer, new ModLiveIpcPeer(
                    association.Identity, isOwner: false, SendFrameAsync, pipe.Dispose));
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            }
            catch
            {
                ModLivePeer?.Close();
                pipe.Dispose();
                throw;
            }
            finally
            {
                association.Dispose();
            }
        }
        _logger.LogInformation("Connected to watcher");
        _listenTask = ListenLoop(pipe, _cts.Token);
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
    
    public async Task SendReadyAsync(string moduleDirectory, CancellationToken cancellationToken = default)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(moduleDirectory);
        await SendAsync(new IpcMessage(IpcMessageType.WorkerReady, payload), cancellationToken);
        _logger.LogInformation("Sent WorkerReady signal");
    }
    
    public async Task SendStateResponseAsync(HotReloadState? state, CancellationToken cancellationToken = default)
    {
        var payload = state?.Serialize() ?? Array.Empty<byte>();
        await SendAsync(new IpcMessage(IpcMessageType.StateResponse, payload), cancellationToken);
    }
    
    public async Task SendShutdownAckAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(new IpcMessage(IpcMessageType.ShutdownAck), cancellationToken);
    }
    
    public async Task RequestHotReloadAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(new IpcMessage(IpcMessageType.HotReloadRequest), cancellationToken);
        _logger.LogInformation("Sent HotReloadRequest to watcher");
    }
    
    private async Task ListenLoop(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        var headerBuffer = new byte[5];
        var live = ModLivePeer;
        try
        {
            while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
            {
                IpcMessage? received = await IpcFrame.ReadAsync(pipe, headerBuffer, cancellationToken);
                if (received is not { } message)
                {
                    _logger.LogInformation("Watcher disconnected");
                    break;
                }
                if (ModLiveIpcProtocol.IsReserved(message.Type))
                {
                    if (live is not null && !live.TryReceive(message)) break;
                    continue;
                }
                if (live is not null && await TryHandleAssociatedRequestAsync(live, message, cancellationToken))
                    continue;
                await HandleMessageAsync(message, cancellationToken);
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
        }
    }

    private async ValueTask<bool> TryHandleAssociatedRequestAsync(
        ModLiveIpcPeer live, IpcMessage request, CancellationToken token)
    {
        switch (request.Type)
        {
            case IpcMessageType.ShutdownRequest:
                live.Close();
                await SendShutdownAckAsync(token);
                if (OnShutdownRequest is { } shutdown)
                {
                    if (_scheduler is { } scheduler) _ = scheduler.InvokeAsync(shutdown);
                    else _ = Task.Run(shutdown);
                }
                return true;
            case IpcMessageType.StateRequest:
            case IpcMessageType.EditorSnapshotRequest:
                if (!live.Loss.IsCompleted && Interlocked.CompareExchange(ref _modLiveCapturePending, 1, 0) == 0)
                {
                    _ = Task.Run(() => RespondToAssociatedCaptureAsync(live, request, token));
                }
                else
                {
                    await SendAsync(new(CaptureResponseType(request.Type)), token);
                }
                return true;
            default:
                return false;
        }
    }

    private async Task RespondToAssociatedCaptureAsync(ModLiveIpcPeer live, IpcMessage request, CancellationToken token)
    {
        try
        {
            byte[]? payload = null;
            try
            {
                if (!live.Loss.IsCompleted && request.Type == IpcMessageType.StateRequest && OnModLiveStateRequest is { } state)
                    payload = (await state(token).ConfigureAwait(false))?.Serialize();
                else if (!live.Loss.IsCompleted && request.Type == IpcMessageType.EditorSnapshotRequest && OnModLiveEditorSnapshotRequest is { } snapshot)
                    payload = (await snapshot(token).ConfigureAwait(false))?.Serialize();
            }
            catch
            {
                // Capture rejection uses the legacy empty-response format.
            }
            if (live.Loss.IsCompleted) payload = null;
            await SendAsync(new(CaptureResponseType(request.Type), payload), token).ConfigureAwait(false);
            if (payload is not null) OnMessageReceived?.Invoke(request);
        }
        catch
        {
            live.Close();
            _pipe?.Dispose();
        }
        finally
        {
            Volatile.Write(ref _modLiveCapturePending, 0);
        }
    }

    private static IpcMessageType CaptureResponseType(IpcMessageType request) => request == IpcMessageType.StateRequest
        ? IpcMessageType.StateResponse
        : IpcMessageType.EditorSnapshotResponse;

    private async Task HandleMessageAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case IpcMessageType.StateRequest:
                _logger.LogInformation("Received StateRequest");
                HotReloadState? state = null;
                
                if (OnStateRequest != null)
                {
                    if (_scheduler != null)
                    {
                        var jobHandle = _scheduler.InvokeAsync(() => OnStateRequest());
                        state = jobHandle.GetAwaiter().GetResult();
                    }
                    else
                    {
                        state = OnStateRequest();
                    }
                }
                
                await SendStateResponseAsync(state, cancellationToken);
                break;
                
            case IpcMessageType.ShutdownRequest:
                _logger.LogInformation("Received ShutdownRequest");
                await SendShutdownAckAsync(cancellationToken);
                if (OnShutdownRequest is { } shutdown)
                {
                    if (_scheduler is { } scheduler)
                    {
                        scheduler.InvokeAsync(shutdown).GetAwaiter().GetResult();
                    }
                    else
                    {
                        shutdown();
                    }
                }
                break;
                
            case IpcMessageType.PingRequest:
                await SendAsync(new IpcMessage(IpcMessageType.PingResponse), cancellationToken);
                break;

            case IpcMessageType.EditorSnapshotRequest:
                byte[] snapshotPayload;
                try
                {
                    EditorRuntimeSnapshot? snapshot;
                    if (OnEditorSnapshotRequest is null)
                    {
                        snapshot = null;
                    }
                    else if (_scheduler is not null)
                    {
                        snapshot = _scheduler.InvokeAsync(OnEditorSnapshotRequest).GetAwaiter().GetResult();
                    }
                    else
                    {
                        snapshot = OnEditorSnapshotRequest();
                    }

                    snapshotPayload = snapshot?.Serialize() ?? Array.Empty<byte>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Editor snapshot failed");
                    snapshotPayload = Array.Empty<byte>();
                }

                await SendAsync(
                    new IpcMessage(
                        IpcMessageType.EditorSnapshotResponse,
                        snapshotPayload),
                    cancellationToken);
                break;
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
            // The listener observes cancellation or the disposed pipe.
        }

        if (_listenTask is { IsCompleted: false } listenTask)
        {
            _ = listenTask.ContinueWith(
                static (_, state) => ((IpcClient)state!).DisposeListenerResources(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return;
        }

        DisposeListenerResources();
    }

    public async Task StopAsync()
    {
        Dispose();
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
