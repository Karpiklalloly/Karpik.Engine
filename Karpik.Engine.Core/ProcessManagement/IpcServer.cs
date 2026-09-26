using System.IO.Pipes;

using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core;

public class IpcServer : IDisposable
{
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
    
    public event Action<IpcMessage>? OnMessageReceived;
    public bool IsConnected => _pipe?.IsConnected ?? false;
    
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
        _pipe = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        
        _logger.LogInformation("Waiting for worker connection on pipe {PipeName}", _pipeName);
        await _pipe.WaitForConnectionAsync(cancellationToken);
        _logger.LogInformation("Worker connected");
        
        // Start listening for messages
        _listenTask = ListenLoop(_cts.Token);
    }
    
    public async Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            if (_pipe == null || !_pipe.IsConnected)
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
    
    private async Task ListenLoop(CancellationToken cancellationToken)
    {
        var headerBuffer = new byte[5]; // 4 bytes length + 1 byte type
        
        try
        {
            while (!cancellationToken.IsCancellationRequested && _pipe?.IsConnected == true)
            {
                // Read header
                var bytesRead = await _pipe.ReadAsync(headerBuffer.AsMemory(0, 5), cancellationToken);
                if (bytesRead == 0)
                {
                    _logger.LogInformation("Worker disconnected");
                    break;
                }
                
                if (bytesRead < 5)
                {
                    // Read remaining header bytes
                    var remaining = 5 - bytesRead;
                    while (remaining > 0)
                    {
                        var n = await _pipe.ReadAsync(headerBuffer.AsMemory(bytesRead, remaining), cancellationToken);
                        if (n == 0) break;
                        bytesRead += n;
                        remaining -= n;
                    }
                }
                
                var (totalLength, type) = IpcMessage.ReadHeader(headerBuffer);
                var payloadLength = totalLength - 5;
                
                // Read payload if any
                var payload = Array.Empty<byte>();
                if (payloadLength > 0)
                {
                    payload = new byte[payloadLength];
                    var payloadRead = 0;
                    while (payloadRead < payloadLength)
                    {
                        var n = await _pipe.ReadAsync(payload.AsMemory(payloadRead, payloadLength - payloadRead), cancellationToken);
                        if (n == 0) break;
                        payloadRead += n;
                    }
                }
                
                var message = new IpcMessage(type, payload);
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
    }
    
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            _pipe?.Dispose();
        }
        catch
        {
            // Ignore pipe disposal errors during worker restart/shutdown.
        }

        try
        {
            if (_listenTask is { IsCompleted: false })
            {
                _listenTask.Wait(TimeSpan.FromMilliseconds(250));
            }
        }
        catch
        {
            // The listen loop is expected to observe cancellation or a disposed pipe.
        }

        _cts.Dispose();
        _ownedLoggerFactory?.Dispose();
    }
}
