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
        _pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        
        _logger.LogInformation("Connecting to watcher on pipe {PipeName}", _pipeName);
        await _pipe.ConnectAsync(TimeSpan.FromSeconds(30), cancellationToken);
        _logger.LogInformation("Connected to watcher");
        
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
                    _logger.LogInformation("Watcher disconnected");
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
    }
    
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
                OnShutdownRequest?.Invoke();
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
