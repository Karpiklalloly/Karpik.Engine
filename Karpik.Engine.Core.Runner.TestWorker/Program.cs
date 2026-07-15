using System.IO.Pipes;

namespace Karpik.Engine.Core.Runner.TestWorker;

public static class WorkerMarker
{
}

internal static class Program
{
    private const byte StateRequest = 0x10;
    private const byte StateResponse = 0x11;
    private const byte ShutdownRequest = 0x20;
    private const byte ShutdownAck = 0x21;
    private const byte HotReloadRequest = 0x32;
    private const byte WorkerReady = 0x40;

    public static async Task Main(string[] args)
    {
        string pipeName = GetRequiredArgument(args, "--pipe-name");
        string bundlePath = GetRequiredArgument(args, "--bundle");
        string controlPath = Path.Combine(bundlePath, "reload", "state", "lifecycle-tests");
        Directory.CreateDirectory(controlPath);
        await File.AppendAllTextAsync(Path.Combine(controlPath, "starts.log"), $"{Environment.ProcessId}\n");

        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(10_000);

        using var sendGate = new SemaphoreSlim(1, 1);
        using var stopped = new CancellationTokenSource();
        Task reloadTask = RequestReloadWhenTriggeredAsync(pipe, sendGate, controlPath, stopped.Token);
        await SendAsync(
            pipe,
            sendGate,
            WorkerReady,
            System.Text.Encoding.UTF8.GetBytes(Path.Combine(bundlePath, "modules.version.1")),
            CancellationToken.None);

        try
        {
            while (!stopped.IsCancellationRequested)
            {
                (byte type, _) = await ReadAsync(pipe, stopped.Token);
                switch (type)
                {
                    case StateRequest:
                        string requested = Path.Combine(controlPath, "state-requested");
                        string release = Path.Combine(controlPath, "release-state");
                        await File.WriteAllTextAsync(requested, "requested", stopped.Token);
                        while (!File.Exists(release))
                        {
                            await Task.Delay(5, stopped.Token);
                        }
                        byte[] state = new byte[sizeof(long) + sizeof(int)];
                        await SendAsync(pipe, sendGate, StateResponse, state, stopped.Token);
                        await File.WriteAllTextAsync(
                            Path.Combine(controlPath, "state-response-sent"),
                            "sent",
                            stopped.Token);
                        stopped.Cancel();
                        break;
                    case ShutdownRequest:
                        await SendAsync(pipe, sendGate, ShutdownAck, [], stopped.Token);
                        stopped.Cancel();
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            stopped.Cancel();
            try
            {
                await reloadTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static async Task RequestReloadWhenTriggeredAsync(
        NamedPipeClientStream pipe,
        SemaphoreSlim sendGate,
        string controlPath,
        CancellationToken cancellationToken)
    {
        string trigger = Path.Combine(controlPath, "request-reload");
        while (!File.Exists(trigger))
        {
            await Task.Delay(5, cancellationToken);
        }
        File.Delete(trigger);
        await SendAsync(pipe, sendGate, HotReloadRequest, [], cancellationToken);
    }

    private static async Task<(byte Type, byte[] Payload)> ReadAsync(
        NamedPipeClientStream pipe,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[5];
        await ReadExactlyAsync(pipe, header, cancellationToken);
        int payloadLength = BitConverter.ToInt32(header, 0);
        byte[] payload = new byte[payloadLength];
        await ReadExactlyAsync(pipe, payload, cancellationToken);
        return (header[4], payload);
    }

    private static async Task ReadExactlyAsync(
        NamedPipeClientStream pipe,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await pipe.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("Watcher disconnected.");
            }
            offset += read;
        }
    }

    private static async Task SendAsync(
        NamedPipeClientStream pipe,
        SemaphoreSlim sendGate,
        byte type,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        await sendGate.WaitAsync(cancellationToken);
        try
        {
            byte[] frame = new byte[5 + payload.Length];
            BitConverter.GetBytes(payload.Length).CopyTo(frame, 0);
            frame[4] = type;
            payload.CopyTo(frame, 5);
            await pipe.WriteAsync(frame, cancellationToken);
            await pipe.FlushAsync(cancellationToken);
        }
        finally
        {
            sendGate.Release();
        }
    }

    private static string GetRequiredArgument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0 || index == args.Length - 1)
        {
            throw new ArgumentException($"Missing required argument {name}.");
        }
        return args[index + 1];
    }
}
