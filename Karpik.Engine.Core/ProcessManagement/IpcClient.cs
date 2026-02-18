using System.IO.Pipes;

namespace Karpik.Engine.Core.ProcessManagement;

/// <summary>
/// IPC Client for the Worker process.
/// Connects to the Watcher process via named pipes.
/// </summary>
public class IpcClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private MainThreadScheduler? _scheduler;
    
    public event Action<IpcMessage>? OnMessageReceived;
    public bool IsConnected => _pipe?.IsConnected ?? false;
    
    // Callbacks for handling requests from watcher
    public Func<HotReloadState?>? OnStateRequest { get; set; }
    public Action? OnShutdownRequest { get; set; }
    
    public IpcClient(string pipeName)
    {
        _pipeName = pipeName;
    }
    
    /// <summary>
    /// Sets the main thread scheduler for executing GPU operations on the main thread.
    /// Must be called before ConnectAsync if state collection involves GPU operations.
    /// </summary>
    public void SetScheduler(MainThreadScheduler scheduler)
    {
        _scheduler = scheduler;
    }
    
    /// <summary>
    /// Connects to the watcher's IPC server.
    /// </summary>
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
        
        // Start listening for messages
        _listenTask = ListenLoop(_cts.Token);
    }
    
    /// <summary>
    /// Sends a message to the watcher process.
    /// </summary>
    public async Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (_pipe == null || !_pipe.IsConnected)
            throw new InvalidOperationException("Pipe is not connected");
        
        var bytes = message.ToBytes();
        await _pipe.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        await _pipe.FlushAsync(cancellationToken);
    }
    
    /// <summary>
    /// Sends a WorkerReady signal to the watcher.
    /// </summary>
    public async Task SendReadyAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(new IpcMessage(IpcMessageType.WorkerReady), cancellationToken);
        Console.WriteLine("[IpcClient] Sent WorkerReady signal");
    }
    
    /// <summary>
    /// Sends state response to the watcher.
    /// </summary>
    public async Task SendStateResponseAsync(HotReloadState? state, CancellationToken cancellationToken = default)
    {
        var payload = state?.Serialize() ?? Array.Empty<byte>();
        await SendAsync(new IpcMessage(IpcMessageType.StateResponse, payload), cancellationToken);
    }
    
    /// <summary>
    /// Sends shutdown acknowledgment to the watcher.
    /// </summary>
    public async Task SendShutdownAckAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(new IpcMessage(IpcMessageType.ShutdownAck), cancellationToken);
    }
    
    /// <summary>
    /// Requests hot reload from the watcher. Worker calls this when it wants to be reloaded.
    /// </summary>
    public async Task RequestHotReloadAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(new IpcMessage(IpcMessageType.HotReloadRequest), cancellationToken);
        Console.WriteLine("[IpcClient] Sent HotReloadRequest to watcher");
    }
    
    /// <summary>
    /// Processes incoming messages. Should be called regularly in the main loop.
    /// </summary>
    public void ProcessMessages()
    {
        // Messages are processed in ListenLoop via events
        // This method can be used for synchronous processing if needed
    }
    
    private async Task ListenLoop(CancellationToken cancellationToken)
    {
        var headerBuffer = new byte[5]; // 4 bytes length + 1 byte type
        
        try
        {
            while (!cancellationToken.IsCancellationRequested && _pipe?.IsConnected == true)
            {
                // Read header
                var bytesRead = await _pipe.ReadAsync(headerBuffer, 0, 5, cancellationToken);
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
                        var n = await _pipe.ReadAsync(headerBuffer, bytesRead, remaining, cancellationToken);
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
                        var n = await _pipe.ReadAsync(payload, payloadRead, payloadLength - payloadRead, cancellationToken);
                        if (n == 0) break;
                        payloadRead += n;
                    }
                }
                
                var message = new IpcMessage(type, payload);
                
                // Handle special messages
                await HandleMessageAsync(message, cancellationToken);
                
                // Notify external handlers
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
                        // Execute state collection on main thread (required for GPU operations)
                        var jobHandle = _scheduler.InvokeAsync(() => OnStateRequest());
                        state = jobHandle.GetAwaiter().GetResult();
                    }
                    else
                    {
                        // No scheduler - execute directly (may crash if GPU operations involved)
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
