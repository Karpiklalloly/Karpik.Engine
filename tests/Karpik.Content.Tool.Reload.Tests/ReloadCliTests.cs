using System.Diagnostics;
using System.Text.Json;
using Karpik.Content.Core;
using Karpik.Content.Transport;
using Xunit;

namespace Karpik.Content.Tool.Reload.Tests;

public sealed class ReloadCliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "karpik-reload-cli-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "source");
    private string Output => Path.Combine(_root, "output");
    private const string Instance = "test-runtime";
    private ContentReloadAddress Address(ContentReloadTarget target = ContentReloadTarget.Client) => ContentReloadAddress.Create(Output, Instance, "game", target);
    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ReloadCliTests()
    {
        Directory.CreateDirectory(Source);
        File.WriteAllText(Path.Combine(Source, "a.json"), "{\"value\":1}");
        File.WriteAllText(Path.Combine(Source, "a.json.meta"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, assetId = Guid.NewGuid().ToString("D"), declaredType = "raw-json",
            logicalName = "game/a", importSettings = new { }, dependencies = Array.Empty<string>()
        }));
    }
    private string[] BuildArgs(string target = "Client") => ["build", "--source", Source, "--output", Output, "--namespace", "game", "--target", target];
    private string[] NotifyArgs(string id, string ns = "game", string target = "Client") =>
        ["notify", "--output", Output, "--generation", id, "--namespace", ns, "--target", target, "--instance", Instance, "--delivery-timeout-ms", "3000"];
    private ContentBuildResult Build(AssetTarget target = AssetTarget.Client) => new ContentBuildCoordinator().Build(new()
    { SourceRoot = Source, OutputRoot = Output, Namespace = "game", Target = target });

    [Fact]
    public async Task BuildNotifyRetainsSuccessfulGenerationAcrossConcurrentPublication()
    {
        var received = Signal<ContentReloadRequest>();
        var release = Signal<bool>();
        await using var server = new ContentReloadEndpoint(Address(), async (request, token) =>
        {
            received.TrySetResult(request);
            await release.Task;
            return new(1, request.RequestId, ContentReloadOutcome.Applied);
        });
        Task<ToolResult> tool = RunToolAsync([.. BuildArgs(), "--notify", Instance, "--delivery-timeout-ms", "5000"]);
        try
        {
            ContentReloadRequest request = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(Path.Combine(Source, "a.json"), "{\"value\":2}");
            using (var newer = Build()) Assert.True(newer.Success);
            using (var old = ContentGeneration.Open(Output, request.GenerationId))
                Assert.Equal("{\"value\":1}", File.ReadAllText(Path.Combine(old.RootPath, old.Manifest.Entries.Single().ArtifactLocator)));
            Assert.Equal("game", request.Namespace);
            Assert.Equal(ContentReloadTarget.Client, request.Target);
            release.TrySetResult(true);
            var result = await tool;
            Assert.Equal(0, result.Code);
            Assert.Contains("build succeeded", result.StdOut);
            Assert.Contains("generation: " + request.GenerationId, result.StdOut);
            Assert.Contains("delivery applied", result.StdOut);
        }
        finally { release.TrySetResult(true); await tool; }
    }

    [Fact]
    public async Task FailedBuildNeverNotifies()
    {
        int calls = 0;
        await using var server = new ContentReloadEndpoint(Address(), (request, token) =>
        { Interlocked.Increment(ref calls); return Task.FromResult(new ContentReloadResponse(1, request.RequestId, ContentReloadOutcome.Applied)); });
        File.WriteAllText(Path.Combine(Source, "a.json"), "{invalid");
        var result = await RunToolAsync([.. BuildArgs(), "--notify", Instance]);
        Assert.Equal(2, result.Code);
        Assert.DoesNotContain("build succeeded", result.StdOut);
        Assert.DoesNotContain("delivery ", result.StdOut);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(ContentReloadOutcome.Applied, 0, "applied", "Client")]
    [InlineData(ContentReloadOutcome.Applied, 0, "applied", "Server")]
    [InlineData(ContentReloadOutcome.Superseded, 0, "superseded", "Client")]
    [InlineData(ContentReloadOutcome.Failed, 4, "failed", "Client")]
    [InlineData(ContentReloadOutcome.Unknown, 5, "unknown", "Client")]
    public async Task BuildSuccessAndDeliveryOutcomeAreSeparate(ContentReloadOutcome outcome, int code, string status, string target)
    {
        await using var server = new ContentReloadEndpoint(Address(target == "Client" ? ContentReloadTarget.Client : ContentReloadTarget.Server),
            (request, token) => Task.FromResult(new ContentReloadResponse(1, request.RequestId, outcome, "test diagnostic")));
        var result = await RunToolAsync([.. BuildArgs(target), "--notify", Instance]);
        Assert.Equal(code, result.Code);
        Assert.Contains("build succeeded", result.StdOut);
        Assert.Contains("delivery " + status, result.StdOut);
        Assert.True(File.Exists(Path.Combine(Output, "manifest.json")));
    }

    [Fact]
    public async Task UnavailableEndpointPreservesBuildAndAllowsRetryWithoutSource()
    {
        var failedWait = await RunToolAsync([.. BuildArgs(), "--notify", Instance, "--delivery-timeout-ms", "100"]);
        Assert.Equal(5, failedWait.Code);
        Assert.Contains("build succeeded", failedWait.StdOut);
        Assert.Contains("delivery unknown", failedWait.StdOut);
        using var generation = ContentGeneration.OpenCurrent(Output);
        Directory.Delete(Source, recursive: true);
        await using var server = new ContentReloadEndpoint(Address(), (request, token) =>
            Task.FromResult(new ContentReloadResponse(1, request.RequestId, ContentReloadOutcome.Applied)));
        var retried = await RunToolAsync(NotifyArgs(generation.GenerationId));
        Assert.Equal(0, retried.Code);
        Assert.Contains("delivery applied", retried.StdOut);
        Assert.DoesNotContain("build succeeded", retried.StdOut);
    }

    [Fact]
    public async Task AcceptedDeliveryTimeoutReportsUnknownWithoutCancelingApplication()
    {
        var received = Signal<CancellationToken>();
        var release = Signal<bool>();
        var completed = Signal<bool>();
        await using var server = new ContentReloadEndpoint(Address(), async (request, token) =>
        {
            received.TrySetResult(token);
            await release.Task;
            completed.TrySetResult(true);
            return new(1, request.RequestId, ContentReloadOutcome.Applied);
        });
        try
        {
            Task<ToolResult> tool = RunToolAsync([.. BuildArgs(), "--notify", Instance, "--delivery-timeout-ms", "300"]);
            CancellationToken applicationToken = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ToolResult result = await tool;
            Assert.Equal(5, result.Code);
            Assert.Contains("build succeeded", result.StdOut);
            Assert.Contains("delivery unknown", result.StdOut);
            Assert.False(applicationToken.IsCancellationRequested);
        }
        finally { release.TrySetResult(true); }
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RetryLeaseProtectsOlderGenerationAcrossConcurrentPublication()
    {
        using var built = Build();
        Assert.True(built.Success);
        string id = built.GenerationId!;
        var received = Signal<bool>();
        var release = Signal<bool>();
        await using var server = new ContentReloadEndpoint(Address(), async (request, token) =>
        {
            received.TrySetResult(true);
            await release.Task;
            return new(1, request.RequestId, ContentReloadOutcome.Applied);
        });
        Task<ToolResult> tool = RunToolAsync(NotifyArgs(id));
        try
        {
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(Path.Combine(Source, "a.json"), "{\"value\":2}");
            using (var newer = Build()) Assert.True(newer.Success);
            using (var retained = ContentGeneration.Open(Output, id))
                Assert.Equal("{\"value\":1}", File.ReadAllText(Path.Combine(retained.RootPath, retained.Manifest.Entries.Single().ArtifactLocator)));
            release.TrySetResult(true);
            Assert.Equal(0, (await tool).Code);
        }
        finally { release.TrySetResult(true); await tool; }
    }

    [Fact]
    public async Task RetryRejectsImportedLegacyGenerationWithoutKnownIdentity()
    {
        Directory.CreateDirectory(Output);
        new ContentManifest(ContentManifest.CurrentSchemaVersion, []).SaveToFile(Path.Combine(Output, "manifest.json"));
        using var imported = ContentGeneration.OpenOrImportCurrent(Output);
        Assert.False(imported.IdentityKnown);
        var result = await RunToolAsync(NotifyArgs(imported.GenerationId));
        Assert.Equal(4, result.Code);
        Assert.Contains("delivery failed", result.StdErr);
    }

    [Theory]
    [InlineData("other", "Client")]
    [InlineData("game", "Server")]
    public async Task RetryRejectsGenerationIdentityBeforeDelivery(string ns, string target)
    {
        using var built = Build();
        Assert.True(built.Success);
        int calls = 0;
        await using var server = new ContentReloadEndpoint(Address(), (request, token) =>
        { Interlocked.Increment(ref calls); return Task.FromResult(new ContentReloadResponse(1, request.RequestId, ContentReloadOutcome.Applied)); });
        var result = await RunToolAsync(NotifyArgs(built.GenerationId!, ns, target));
        Assert.Equal(4, result.Code);
        Assert.Contains("delivery failed", result.StdErr);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("--notify", "bad/instance")]
    [InlineData("--delivery-timeout-ms", "0")]
    [InlineData("--delivery-timeout-ms", "-1")]
    [InlineData("--delivery-timeout-ms", "not-a-number")]
    [InlineData("--delivery-timeout-ms", "86400001")]
    public async Task InvalidBuildDeliveryOptionsAreUsageBeforePublication(string flag, string value)
    {
        var result = await RunToolAsync([.. BuildArgs(), flag, value]);
        Assert.Equal(1, result.Code);
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public async Task TimeoutWithoutNotifyAndDuplicateNotifyAreUsage()
    {
        Assert.Equal(1, (await RunToolAsync([.. BuildArgs(), "--delivery-timeout-ms", "100"])).Code);
        Assert.Equal(1, (await RunToolAsync([.. BuildArgs(), "--notify", Instance, "--notify", "another"])).Code);
        Assert.False(Directory.Exists(Output));
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task NotifyMissingInstanceDoesNotConsumeHelpFlagOrPublish(string help)
    {
        var result = await RunToolAsync([.. BuildArgs(), "--notify", help, "--delivery-timeout-ms", "100"]);
        Assert.Equal(1, result.Code);
        string[] retry = NotifyArgs(Guid.NewGuid().ToString("N"));
        retry[Array.IndexOf(retry, "--instance") + 1] = help;
        Assert.Equal(1, (await RunToolAsync(retry)).Code);
        Assert.False(Directory.Exists(Output));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("")]
    public async Task InvalidRetryGenerationIsUsageBeforeFilesystemAccess(string generation)
    {
        var result = await RunToolAsync(NotifyArgs(generation));
        Assert.Equal(1, result.Code);
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public async Task MissingPublishedGenerationIsDeliveryFailure()
    {
        var result = await RunToolAsync(NotifyArgs(Guid.NewGuid().ToString("N")));
        Assert.Equal(4, result.Code);
        Assert.Contains("delivery failed", result.StdErr);
        Assert.DoesNotContain("unexpected error", result.StdErr);
    }

    [Fact]
    public async Task OrdinaryBuildHasNoDeliveryOutputAndHelpDocumentsRetryCodes()
    {
        var result = await RunToolAsync(BuildArgs());
        Assert.Equal(0, result.Code);
        Assert.Contains("build succeeded", result.StdOut);
        Assert.DoesNotContain("delivery ", result.StdOut);
        Assert.DoesNotContain("generation: ", result.StdOut);
        var help = await RunToolAsync("help");
        Assert.Contains("--notify", help.StdOut);
        Assert.Contains("content notify", help.StdOut);
        Assert.Contains("4=delivery failed", help.StdOut);
        Assert.Contains("5=delivery unknown", help.StdOut);
    }

    private static async Task<ToolResult> RunToolAsync(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "content.dll"));
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return new(process.ExitCode, await stdout, await stderr);
    }
    private readonly record struct ToolResult(int Code, string StdOut, string StdErr);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
