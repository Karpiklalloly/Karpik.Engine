using System.IO.Pipes;

namespace Karpik.Engine.Core;

internal class IpcClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private MainThreadScheduler? _scheduler;
    
    public event Action<IpcMessage>? OnMessageReceived;
    public bool IsConnected => _pipe?.IsConnected ?? false;
    
    public Func<HotReloadState?>? OnStateRequest { get; set; }
    public Action? OnShutdownRequest { get; set; }
    
    public IpcClient(string pipeName)
    {
        _pipeName = pipeName;
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
        
        Console.WriteLine($"[IpcClient] Connecting to watcher on pipe: {_pipeName}");
        await _pipe.ConnectAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Console.WriteLine("[IpcClient] Connected to watcher!");
        
        _listenTask = ListenLoop(_cts.Token);
    }
    
    public async Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (_pipe == null || !_pipe.IsConnected)
            throw new InvalidOperationException("Pipe is not connected");
        
        var bytes = message.ToBytes();
        await _pipe.WriteAsync(bytes, cancellationToken);
        await _pipe.FlushAsync(cancellationToken);
    }
    
    public async Task SendReadyAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(new IpcMessage(IpcMessageType.WorkerReady), cancellationToken);
        Console.WriteLine("[IpcClient] Sent WorkerReady signal");
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
        Console.WriteLine("[IpcClient] Sent HotReloadRequest to watcher");
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
                    Console.WriteLine("[IpcClient] Watcher disconnected");
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
            Console.WriteLine($"[IpcClient] Pipe error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IpcClient] Listen error: {ex}");
        }
    }
    
    private async Task HandleMessageAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case IpcMessageType.StateRequest:
                Console.WriteLine("[IpcClient] Received StateRequest");
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
                Console.WriteLine("[IpcClient] Received ShutdownRequest");
                await SendShutdownAckAsync(cancellationToken);
                OnShutdownRequest?.Invoke();
                break;
                
            case IpcMessageType.PingRequest:
                await SendAsync(new IpcMessage(IpcMessageType.PingResponse), cancellationToken);
                break;
        }
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        _pipe?.Dispose();
        _cts.Dispose();
    }
}
