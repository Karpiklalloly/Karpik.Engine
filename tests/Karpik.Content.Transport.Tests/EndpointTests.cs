using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Karpik.Content.Transport;
using Xunit;

namespace Karpik.Content.Transport.Tests;

public sealed class EndpointTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "karpik-transport-" + Guid.NewGuid().ToString("N"));
    private ContentReloadAddress Address => ContentReloadAddress.Create(_root, "runtime-1", "sample", ContentReloadTarget.Client);
    private ContentReloadRequest Request => new(1, Guid.NewGuid(), _root, "sample", ContentReloadTarget.Client, Guid.NewGuid().ToString("N"));
    private static Task<ContentReloadResponse> Applied(ContentReloadRequest request, CancellationToken token) =>
        Task.FromResult(new ContentReloadResponse(1, request.RequestId, ContentReloadOutcome.Applied));
    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task DeliversAndCorrelatesTerminalResponse()
    {
        await using var server = new ContentReloadEndpoint(Address, Applied);
        ContentReloadRequest request = Request;
        var response = await ContentReloadClient.DeliverAsync(Address, request, TimeSpan.FromSeconds(5));
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(ContentReloadOutcome.Applied, response.Outcome);
    }

    [Fact]
    public async Task DuplicateEndpointRejectedAndDisposeAllowsRestart()
    {
        await using (var server = new ContentReloadEndpoint(Address, Applied))
            Assert.Throws<IOException>(() => new ContentReloadEndpoint(Address, Applied));
        await using var restarted = new ContentReloadEndpoint(Address, Applied);
        Assert.Equal(ContentReloadOutcome.Applied, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5))).Outcome);
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("target")]
    [InlineData("root")]
    [InlineData("generation")]
    [InlineData("version")]
    public async Task RejectsMismatchBeforeHandler(string field)
    {
        int calls = 0;
        await using var server = new ContentReloadEndpoint(Address, (request, token) => { Interlocked.Increment(ref calls); return Applied(request, token); });
        var request = Request;
        request = field switch
        {
            "namespace" => request with { Namespace = "other" },
            "target" => request with { Target = ContentReloadTarget.Server },
            "root" => request with { OutputRoot = Path.Combine(_root, "other") },
            "generation" => request with { GenerationId = "../not-a-generation" },
            _ => request with { ProtocolVersion = 99 }
        };
        Assert.Equal(ContentReloadOutcome.Failed, (await ContentReloadClient.DeliverAsync(Address, request, TimeSpan.FromSeconds(5))).Outcome);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task TimeoutDoesNotCancelAcceptedApplicationAndRetryCanSucceed()
    {
        var started = Signal<bool>();
        var release = Signal<bool>();
        var completed = Signal<bool>();
        int calls = 0;
        await using var server = new ContentReloadEndpoint(Address, async (request, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.TrySetResult(true);
                await release.Task;
                Assert.False(token.IsCancellationRequested);
                completed.TrySetResult(true);
            }
            return await Applied(request, token);
        });
        var pending = ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromMilliseconds(300));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ContentReloadOutcome.Unknown, (await pending).Outcome);
        release.TrySetResult(true);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ContentReloadOutcome.Applied, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5))).Outcome);
    }

    [Fact]
    public async Task UnhandledAcceptedHandlerExceptionIsUnknownAndEndpointRemainsUsable()
    {
        int calls = 0;
        await using var server = new ContentReloadEndpoint(Address, (request, token) =>
            Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("staging rejected") : Applied(request, token));
        Assert.Equal(ContentReloadOutcome.Unknown, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5))).Outcome);
        Assert.Equal(ContentReloadOutcome.Applied, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5))).Outcome);
    }

    [Fact]
    public async Task InvalidAcceptedHandlerResultCannotAssertFailure()
    {
        await using var server = new ContentReloadEndpoint(Address, (request, token) =>
            Task.FromResult(new ContentReloadResponse(99, Guid.NewGuid(), ContentReloadOutcome.Applied)));
        Assert.Equal(ContentReloadOutcome.Unknown, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5))).Outcome);
    }

    [Theory]
    [InlineData(ContentReloadOutcome.Applied)]
    [InlineData(ContentReloadOutcome.Superseded)]
    [InlineData(ContentReloadOutcome.Failed)]
    public async Task ExplicitTerminalHandlerOutcomeIsPreserved(ContentReloadOutcome outcome)
    {
        await using var server = new ContentReloadEndpoint(Address, (request, token) =>
            Task.FromResult(new ContentReloadResponse(1, request.RequestId, outcome)));
        Assert.Equal(outcome, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5))).Outcome);
    }

    [Fact]
    public async Task RejectsOversizedAndMalformedFramesAndRecovers()
    {
        int calls = 0;
        await using var server = new ContentReloadEndpoint(Address, (request, token) => { Interlocked.Increment(ref calls); return Applied(request, token); });
        foreach (int length in new[] { -1, 0, int.MaxValue })
        {
            using var pipe = await ConnectAsync();
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, length);
            await pipe.WriteAsync(header);
            Assert.Equal(ContentReloadOutcome.Failed, (await ReadResponseAsync(pipe)).Outcome);
        }
        using (var pipe = await ConnectAsync())
        {
            await WriteFrameAsync(pipe, Encoding.UTF8.GetBytes("{broken"));
            Assert.Equal(ContentReloadOutcome.Failed, (await ReadResponseAsync(pipe)).Outcome);
        }
        Assert.Equal(0, calls);
        Assert.Equal(ContentReloadOutcome.Applied, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5))).Outcome);
    }

    [Fact]
    public async Task StalledReaderReleasesBoundedSlotAfterReadDeadline()
    {
        await using var server = new ContentReloadEndpoint(Address, Applied, maximumConnections: 1, messageTimeout: TimeSpan.FromMilliseconds(150));
        using var stalled = await ConnectAsync();
        Assert.Equal(ContentReloadOutcome.Failed, (await ReadResponseAsync(stalled)).Outcome);
        Assert.Equal(ContentReloadOutcome.Applied, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5))).Outcome);
    }

    [Fact]
    public async Task AcceptedHandlersRemainBoundedAfterClientTimeouts()
    {
        var release = Signal<bool>();
        var twoStarted = Signal<bool>();
        int calls = 0;
        await using var server = new ContentReloadEndpoint(Address, async (request, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2) twoStarted.TrySetResult(true);
            await release.Task;
            return await Applied(request, token);
        }, maximumConnections: 2);
        var first = ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromMilliseconds(300));
        var second = ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromMilliseconds(300));
        await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(first, second);
        Assert.Equal(ContentReloadOutcome.Unknown, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromMilliseconds(100))).Outcome);
        Assert.Equal(2, Volatile.Read(ref calls));
        release.TrySetResult(true);
    }

    [Fact]
    public async Task DefaultCapacityNeverStartsNinthHandlerAfterEightClientsStopWaiting()
    {
        var release = Signal<bool>();
        var eightStarted = Signal<bool>();
        using var stopWaiting = new CancellationTokenSource();
        int calls = 0;
        await using var server = new ContentReloadEndpoint(Address, async (request, token) =>
        {
            if (Interlocked.Increment(ref calls) == 8) eightStarted.TrySetResult(true);
            await release.Task;
            return await Applied(request, token);
        });
        var pending = Enumerable.Range(0, 8)
            .Select(_ => ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(10), stopWaiting.Token)).ToArray();
        try
        {
            await eightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopWaiting.Cancel();
            Assert.All(await Task.WhenAll(pending), response => Assert.Equal(ContentReloadOutcome.Unknown, response.Outcome));
            Assert.Equal(ContentReloadOutcome.Unknown, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromMilliseconds(100))).Outcome);
            Assert.Equal(8, Volatile.Read(ref calls));
        }
        finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task StrictProtocolRejectsUnknownRequestFieldsAndBoundsHandlerErrors()
    {
        int calls = 0;
        await using var server = new ContentReloadEndpoint(Address, (request, token) =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException(new string('x', 4000));
        });
        using (var pipe = await ConnectAsync())
        {
            string json = System.Text.Json.JsonSerializer.Serialize(Request);
            await WriteFrameAsync(pipe, Encoding.UTF8.GetBytes(json[..^1] + ",\"Unexpected\":true}"));
            Assert.Equal(ContentReloadOutcome.Failed, (await ReadResponseAsync(pipe)).Outcome);
        }
        Assert.Equal(0, Volatile.Read(ref calls));
        var response = await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5));
        Assert.Equal(ContentReloadOutcome.Unknown, response.Outcome);
        Assert.True(response.Error?.Length <= 2048);
    }

    [Fact]
    public async Task ShutdownRetainsTargetOwnershipUntilAcceptedHandlerSettles()
    {
        var entered = Signal<bool>();
        var finish = Signal<bool>();
        var server = new ContentReloadEndpoint(Address, async (request, token) =>
        {
            entered.TrySetResult(true);
            await finish.Task;
            return await Applied(request, token);
        });
        var pending = ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await server.DisposeAsync();
        Assert.False(server.Completion.IsCompleted);
        Assert.Throws<IOException>(() => new ContentReloadEndpoint(Address, Applied));
        finish.TrySetResult(true);
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ContentReloadOutcome.Unknown, (await pending).Outcome);
        await using var restarted = new ContentReloadEndpoint(Address, Applied);
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("target")]
    [InlineData("root")]
    [InlineData("generation")]
    [InlineData("version")]
    [InlineData("request")]
    public async Task ServerIndependentlyRejectsRawIdentityMismatch(string field)
    {
        int calls = 0;
        await using var server = new ContentReloadEndpoint(Address, (request, token) => { Interlocked.Increment(ref calls); return Applied(request, token); });
        var request = Request;
        request = field switch
        {
            "namespace" => request with { Namespace = "other" },
            "target" => request with { Target = (ContentReloadTarget)999 },
            "root" => request with { OutputRoot = Path.Combine(_root, "other") },
            "generation" => request with { GenerationId = new string('0', 32) },
            "request" => request with { RequestId = Guid.Empty },
            _ => request with { ProtocolVersion = 99 }
        };
        using var pipe = await ConnectAsync();
        await WriteFrameAsync(pipe, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request));
        Assert.Equal(ContentReloadOutcome.Failed, (await ReadResponseAsync(pipe)).Outcome);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("request")]
    [InlineData("outcome")]
    public async Task ClientRejectsUncorrelatedOrUnsupportedServerResponse(string field)
    {
        var request = Request;
        using var server = new NamedPipeServerStream(Address.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Task responseTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            byte[] header = new byte[4];
            await server.ReadExactlyAsync(header);
            byte[] bytes = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
            await server.ReadExactlyAsync(bytes);
            var response = new ContentReloadResponse(1, request.RequestId, ContentReloadOutcome.Applied);
            response = field switch
            {
                "version" => response with { ProtocolVersion = 99 },
                "request" => response with { RequestId = Guid.NewGuid() },
                _ => response with { Outcome = (ContentReloadOutcome)999 }
            };
            await WriteFrameAsync(server, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(response));
        });
        Assert.Equal(ContentReloadOutcome.Unknown, (await ContentReloadClient.DeliverAsync(Address, request, TimeSpan.FromSeconds(5))).Outcome);
        await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShutdownOfUnresponsiveAcceptedHandlerReturnsBoundedly()
    {
        var entered = Signal<bool>();
        var finish = Signal<bool>();
        var server = new ContentReloadEndpoint(Address, async (request, token) =>
        {
            entered.TrySetResult(true);
            await finish.Task;
            return await Applied(request, token);
        });
        var request = ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ContentReloadOutcome.Unknown, (await request).Outcome);
        finish.TrySetResult(true);
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UnavailableEndpointHasUnknownOutcomeAtDeadline()
    {
        Assert.Equal(ContentReloadOutcome.Unknown,
            (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromMilliseconds(100))).Outcome);
    }

    [Fact]
    public async Task CrossProcessEndpointDeliveryAndCrashReleaseAllowRestart()
    {
        using var child = StartHost();
        Task<string> errors = child.StandardError.ReadToEndAsync();
        try
        {
            Assert.Equal("ready", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Throws<IOException>(() => new ContentReloadEndpoint(Address, Applied));
            Assert.Equal(ContentReloadOutcome.Applied, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5))).Outcome);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal("", await errors);
        await using var restarted = new ContentReloadEndpoint(Address, Applied);
        Assert.Equal(ContentReloadOutcome.Applied, (await ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5))).Outcome);
    }

    [Fact]
    public async Task RuntimeDeathAfterAcceptanceReturnsUnknown()
    {
        using var child = StartHost(block: true);
        Task<string> errors = child.StandardError.ReadToEndAsync();
        try
        {
            Assert.Equal("ready", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            var pending = ContentReloadClient.DeliverAsync(Address, Request, TimeSpan.FromSeconds(5));
            Assert.Equal("accepted", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ContentReloadOutcome.Unknown, (await pending).Outcome);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal("", await errors);
    }

    private Process StartHost(bool block = false)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string assembly = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../EndpointHost/bin", configuration,
            "net10.0/ContentReloadEndpointHost.dll"));
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add(_root);
        if (block) start.ArgumentList.Add("block");
        return Process.Start(start)!;
    }

    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        var pipe = new NamedPipeClientStream(".", Address.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        return pipe;
    }
    private static async Task WriteFrameAsync(Stream stream, byte[] payload)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }
    private static async Task<ContentReloadResponse> ReadResponseAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, timeout.Token);
        byte[] payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
        await stream.ReadExactlyAsync(payload, timeout.Token);
        return System.Text.Json.JsonSerializer.Deserialize<ContentReloadResponse>(payload);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
