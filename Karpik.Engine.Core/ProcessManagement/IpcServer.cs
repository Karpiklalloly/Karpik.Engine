using System.IO.Pipes;

namespace Karpik.Engine.Core;

public class IpcServer : IDisposable
{
    private NamedPipeServerStream? _pipe;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    
    public event Action<IpcMessage>? OnMessageReceived;
    public bool IsConnected => _pipe?.IsConnected ?? false;
    
    public IpcServer(string pipeName)
    {
        _pipeName = pipeName;
    }
    
    public async Task WaitForConnectionAsync(CancellationToken cancellationToken = default)
    {
        _pipe = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        
        Console.WriteLine($"[IpcServer] Waiting for worker connection on pipe: {_pipeName}");
        await _pipe.WaitForConnectionAsync(cancellationToken);
        Console.WriteLine("[IpcServer] Worker connected!");
        
        // Start listening for messages
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
    
    public async Task<HotReloadState?> RequestStateAsync(CancellationToken cancellationToken = default)
    {
        var (_, state) = await TryRequestStateAsync(TimeSpan.FromSeconds(10), cancellationToken);
        return state;
    }

    public async Task<(bool Received, HotReloadState? State)> TryRequestStateAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(new IpcMessage(IpcMessageType.StateRequest), cancellationToken);
        
        var tcs = new TaskCompletionSource<HotReloadState?>();
        
        void Handler(IpcMessage msg)
        {
            if (msg.Type == IpcMessageType.StateResponse)
            {
                OnMessageReceived -= Handler;
                if (msg.Payload.Length > 0)
                {
                    var state = HotReloadState.Deserialize(msg.Payload);
                    tcs.SetResult(state);
                }
                else
                {
                    tcs.SetResult(null);
                }
            }
        }
        
        OnMessageReceived += Handler;
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        
        try
        {
            return (true, await tcs.Task.WaitAsync(cts.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            OnMessageReceived -= Handler;
            Console.WriteLine("[IpcServer] Timeout waiting for StateResponse");
            return (false, null);
        }
    }
    
    public Task SendShutdownRequestAsync(CancellationToken cancellationToken = default)
    {
        return SendShutdownRequestAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    public async Task SendShutdownRequestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        void Handler(IpcMessage msg)
        {
            if (msg.Type == IpcMessageType.ShutdownAck)
            {
                OnMessageReceived -= Handler;
                tcs.SetResult(true);
            }
        }
        
        OnMessageReceived += Handler;
        await SendAsync(new IpcMessage(IpcMessageType.ShutdownRequest), cancellationToken);
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        
        try
        {
            await tcs.Task.WaitAsync(cts.Token);
            Console.WriteLine("[IpcServer] Worker acknowledged shutdown");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            OnMessageReceived -= Handler;
            Console.WriteLine("[IpcServer] Timeout waiting for ShutdownAck, will force kill");
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
                    Console.WriteLine("[IpcServer] Worker disconnected");
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
            Console.WriteLine($"[IpcServer] Pipe error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IpcServer] Listen error: {ex}");
        }
    }
    
    public void Dispose()
    {
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
    }
}
