using Karpik.Engine.Core;
using Karpik.Engine.ProjectModel;
using Karpik.Engine.Tooling;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class PreviewIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task PreviewWorker_StartsProvidesSnapshotAndStops()
    {
        ProjectRuntimeDescriptor runtime = RequireRuntimeDescriptor("KARPIK_RUN_EDITOR_INTEGRATION");
        ProjectRuntimeResolver.EditorRuntimeDescriptor client =
            new ProjectRuntimeResolver(runtime).Resolve(Side.Client);
        using var controller = new EditorPreviewController(
            new RuntimeLaunchOptions(Side.Client, client.RunnerPath, client.BundlePath, client.EngineRoot));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        try
        {
            await controller.StartAsync(cts.Token);
            EditorRuntimeSnapshot? snapshot = await controller.RequestSnapshotAsync(TimeSpan.FromSeconds(2), cts.Token);

            Assert.Equal(EditorPreviewState.Running, controller.State);
            Assert.NotNull(snapshot);
        }
        finally
        {
            if (controller.State != EditorPreviewState.Stopped)
            {
                await controller.StopAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SessionManager_StartsServerThenMultipleClientsAndStopsThemTogether()
    {
        ProjectRuntimeDescriptor runtime = RequireRuntimeDescriptor("KARPIK_RUN_EDITOR_MULTISESSION_INTEGRATION");
        using var manager = new EditorSessionManager(
            new EditorPreviewBackendFactory(new ProjectRuntimeResolver(runtime)));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var serverConnectionCount = 0;
        manager.OutputReceived += (session, line) =>
        {
            output.Enqueue($"[{session.Name}] {line}");
            if (session.Side == Side.Server && line.Contains("Player connected:", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref serverConnectionCount);
            }
        };

        try
        {
            await manager.StartServerAsync(cts.Token);
            await manager.AddClientAsync(cts.Token);
            await manager.AddClientAsync(cts.Token);

            Assert.Collection(
                manager.Sessions,
                server => Assert.Equal(Side.Server, server.Side),
                firstClient => Assert.Equal(Side.Client, firstClient.Side),
                secondClient => Assert.Equal(Side.Client, secondClient.Side));
            Assert.All(manager.Sessions, session =>
            {
                Assert.Equal(EditorPreviewState.Running, session.State);
                Assert.NotNull(session.ProcessId);
            });
            Assert.Equal(3, manager.Sessions.Select(session => session.ProcessId).Distinct().Count());

            var snapshotFailures = new List<string>();
            foreach (EditorSession session in manager.Sessions)
            {
                manager.SelectSession(session);
                try
                {
                    Assert.NotNull(await manager.RequestSelectedSnapshotAsync(TimeSpan.FromSeconds(2), cts.Token));
                }
                catch (Exception ex)
                {
                    snapshotFailures.Add($"{session.Name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Assert.True(
                snapshotFailures.Count == 0,
                $"Snapshot failures: {string.Join("; ", snapshotFailures)}. Output: {string.Join(Environment.NewLine, output.TakeLast(40))}");

            DateTime connectionDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (Volatile.Read(ref serverConnectionCount) < 2 && DateTime.UtcNow < connectionDeadline)
            {
                await Task.Delay(50, cts.Token);
            }
            Assert.True(
                Volatile.Read(ref serverConnectionCount) >= 2,
                $"Server observed only {serverConnectionCount} clients. Output: {string.Join(Environment.NewLine, output.TakeLast(40))}");

            await manager.StopSessionAsync(manager.Sessions[0], cts.Token);
            Assert.All(manager.Sessions, session => Assert.Equal(EditorPreviewState.Stopped, session.State));
        }
        finally
        {
            await manager.StopAllAsync(CancellationToken.None);
        }
    }

    private static ProjectRuntimeDescriptor RequireRuntimeDescriptor(string optInVariable)
    {
        string? engineRoot = Environment.GetEnvironmentVariable("KARPIK_TEST_ENGINE_ROOT");
        string? gameRoot = Environment.GetEnvironmentVariable("KARPIK_TEST_GAME_ROOT");
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(optInVariable) == "1"
            && !string.IsNullOrWhiteSpace(engineRoot)
            && !string.IsNullOrWhiteSpace(gameRoot),
            $"Set {optInVariable}=1, KARPIK_TEST_ENGINE_ROOT, and KARPIK_TEST_GAME_ROOT to run this integration test.");

        string validatedEngineRoot = Path.GetFullPath(engineRoot!);
        EngineInstallationValidationResult validation = new EngineInstallationValidator().Validate(validatedEngineRoot);
        Assert.True(validation.IsValid, validation.Message);

        string activeGameRoot = Path.GetFullPath(gameRoot!);
        Assert.True(Directory.Exists(activeGameRoot), $"Game root was not found: {activeGameRoot}");
        return new ProjectRuntimeDescriptor(
            validatedEngineRoot,
            GetGameBundlePath(activeGameRoot, "KarpikEngineGame.Client"),
            GetGameBundlePath(activeGameRoot, "KarpikEngineGame.Server"),
            GetInstalledRunnerPath(validatedEngineRoot, "client"),
            GetInstalledRunnerPath(validatedEngineRoot, "server"));
    }

    private static string GetGameBundlePath(string gameRoot, string projectName) =>
        Path.Combine(gameRoot, "Source", projectName, "bin", "Debug", "net10.0", "karpik-bundle");

    private static string GetInstalledRunnerPath(string engineRoot, string side)
    {
        string runnerDirectory = Path.Combine(engineRoot, "runners", side);
        string executable = Path.Combine(
            runnerDirectory,
            OperatingSystem.IsWindows() ? "Karpik.Engine.Core.Runner.exe" : "Karpik.Engine.Core.Runner");
        return File.Exists(executable)
            ? executable
            : Path.Combine(runnerDirectory, "Karpik.Engine.Core.Runner.dll");
    }
}
