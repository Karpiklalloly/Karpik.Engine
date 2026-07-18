using Karpik.Engine.Core;
using Karpik.Engine.ProjectModel;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class PreviewIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task PreviewWorker_StartsProvidesSnapshotAndStops()
    {
        if (Environment.GetEnvironmentVariable("KARPIK_RUN_EDITOR_INTEGRATION") != "1")
        {
            return;
        }

        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string workerName = OperatingSystem.IsWindows()
            ? "Karpik.Engine.Core.Runner.exe"
            : "Karpik.Engine.Core.Runner";
        string workerPath = Path.Combine(
            repositoryRoot,
            "Karpik.Editor",
            "bin",
            "Debug",
            "net10.0",
            "runtimes",
            "client",
            workerName);
        using var controller = new EditorPreviewController(new RuntimeLaunchOptions(Side.Client, workerPath, Path.GetTempPath()));
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
        if (Environment.GetEnvironmentVariable("KARPIK_RUN_EDITOR_MULTISESSION_INTEGRATION") != "1")
        {
            return;
        }

        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string editorOutput = Path.Combine(repositoryRoot, "Karpik.Editor", "bin", "Debug", "net10.0");
        using var manager = new EditorSessionManager(
            new EditorPreviewBackendFactory(new ProjectRuntimeResolver(CreateRuntimeDescriptor(editorOutput))));
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

    private static ProjectRuntimeDescriptor CreateRuntimeDescriptor(string engineRoot)
    {
        string runnerName = OperatingSystem.IsWindows()
            ? "Karpik.Engine.Core.Runner.exe"
            : "Karpik.Engine.Core.Runner";
        return new ProjectRuntimeDescriptor(
            engineRoot,
            Path.Combine(engineRoot, "runtimes", "client", "karpik-bundle"),
            Path.Combine(engineRoot, "runtimes", "server", "karpik-bundle"),
            Path.Combine(engineRoot, "runtimes", "client", runnerName),
            Path.Combine(engineRoot, "runtimes", "server", runnerName));
    }
}
