using System.Diagnostics;
using Karpik.Editor;
using Karpik.Engine.ProjectModel;
using Xunit;

namespace Karpik.Editor.Tests.Projects;

public sealed class EditorProjectLifetimeTests
{
    [Fact]
    public async Task BuildAsync_UsesDefaultMsBuildParallelism()
    {
        string root = CreateRoot();
        try
        {
            string solutionPath = Path.Combine(root, "Game.slnx");
            var processes = new RecordingProcessFactory();
            await using var lifetime = new EditorProjectLifetime(
                new WorkspaceStore(Path.Combine(root, "workspace.json")),
                new KarpikSolutionModel(solutionPath, "0.6.0-test", []),
                CreateRuntime(root),
                processes);

            await lifetime.BuildAsync(static _ => { }, TestContext.Current.CancellationToken);

            ProcessStartInfo startInfo = Assert.Single(processes.StartInfos);
            Assert.Equal(["build", solutionPath, "-nr:false", "-c", "Debug"], startInfo.ArgumentList);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PublishAsync_UsesValidatedRuntimeProjectPathsWithoutNamingConventions()
    {
        string root = CreateRoot();
        try
        {
            string solutionPath = Path.Combine(root, "Game.slnx");
            string clientPath = Path.Combine(root, "Source", "My.Client.Runtime.csproj");
            string serverPath = Path.Combine(root, "Dedicated", "ServerHost.csproj");
            var solution = new KarpikSolutionModel(
                solutionPath,
                "0.6.0-test",
                [
                    CreateProject(clientPath, KarpikProjectSide.Client),
                    CreateProject(serverPath, KarpikProjectSide.Server)
                ]);
            var processes = new RecordingProcessFactory();
            await using var lifetime = new EditorProjectLifetime(
                new WorkspaceStore(Path.Combine(root, "workspace.json")),
                solution,
                CreateRuntime(root),
                processes);

            await lifetime.PublishAsync(static _ => { }, TestContext.Current.CancellationToken);

            Assert.Collection(
                processes.StartInfos,
                item => Assert.Equal(
                    ["publish", clientPath, "-nr:false", "-c", "Debug"],
                    item.ArgumentList),
                item => Assert.Equal(
                    ["publish", serverPath, "-nr:false", "-c", "Debug"],
                    item.ArgumentList));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CancelActiveBuildAsync_KillsTreeAndWaitsForConfirmedExit()
    {
        string root = CreateRoot();
        try
        {
            var process = new BlockingProcess();
            var processes = new RecordingProcessFactory(process);
            var solution = new KarpikSolutionModel(
                Path.Combine(root, "Game.slnx"),
                "0.6.0-test",
                []);
            await using var lifetime = new EditorProjectLifetime(
                new WorkspaceStore(Path.Combine(root, "workspace.json")),
                solution,
                CreateRuntime(root),
                processes);
            Task build = lifetime.BuildAsync(static _ => { }, CancellationToken.None);
            await process.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            await lifetime.CancelActiveBuildAsync(TestContext.Current.CancellationToken);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
            Assert.True(process.KillRequested);
            Assert.True(process.ExitWaitCompleted);
            Assert.True(process.Disposed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveWorkspaceAsync_PreservesExistingWindowAndPanelState()
    {
        string root = CreateRoot();
        try
        {
            string workspacePath = Path.Combine(root, "workspace.json");
            var store = new WorkspaceStore(workspacePath);
            await store.SaveAsync(
                new EditorWorkspace
                {
                    SolutionPath = Path.Combine(root, "Previous.slnx"),
                    UiDensity = EditorUiDensity.UltraCompact,
                    LayoutPreset = EditorLayoutPreset.Debug,
                    WindowWidth = 1777,
                    WindowHeight = 999,
                    LeftPanelWidth = 411,
                    BottomPanelHeight = 288
                },
                TestContext.Current.CancellationToken);
            string solutionPath = Path.Combine(root, "Game.slnx");
            var solution = new KarpikSolutionModel(solutionPath, "0.6.0-test", []);
            await using var lifetime = new EditorProjectLifetime(
                store,
                solution,
                CreateRuntime(root));

            await lifetime.SaveWorkspaceAsync(
                solutionPath,
                TestContext.Current.CancellationToken);

            EditorWorkspace saved = await store.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(solutionPath, saved.SolutionPath);
            Assert.Equal(EditorUiDensity.UltraCompact, saved.UiDensity);
            Assert.Equal(EditorLayoutPreset.Debug, saved.LayoutPreset);
            Assert.Equal(1777, saved.WindowWidth);
            Assert.Equal(999, saved.WindowHeight);
            Assert.Equal(411, saved.LeftPanelWidth);
            Assert.Equal(288, saved.BottomPanelHeight);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_WhenOutputReadFails_StillDisposesProcess()
    {
        string root = CreateRoot();
        try
        {
            var process = new FaultingOutputProcess();
            var solution = new KarpikSolutionModel(
                Path.Combine(root, "Game.slnx"),
                "0.6.0-test",
                []);
            await using var lifetime = new EditorProjectLifetime(
                new WorkspaceStore(Path.Combine(root, "workspace.json")),
                solution,
                CreateRuntime(root),
                new RecordingProcessFactory(process));

            await Assert.ThrowsAsync<IOException>(
                () => lifetime.BuildAsync(static _ => { }, TestContext.Current.CancellationToken));

            Assert.True(process.Disposed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CancelActiveBuildAsync_WhenOutputReadFails_StillCompletesCancellation()
    {
        string root = CreateRoot();
        try
        {
            var process = new BlockingFaultingOutputProcess();
            var solution = new KarpikSolutionModel(
                Path.Combine(root, "Game.slnx"),
                "0.6.0-test",
                []);
            await using var lifetime = new EditorProjectLifetime(
                new WorkspaceStore(Path.Combine(root, "workspace.json")),
                solution,
                CreateRuntime(root),
                new RecordingProcessFactory(process));
            Task build = lifetime.BuildAsync(static _ => { }, CancellationToken.None);
            await process.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            await lifetime.CancelActiveBuildAsync(TestContext.Current.CancellationToken);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
            Assert.True(process.Disposed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static KarpikProjectDescriptor CreateProject(
        string path,
        KarpikProjectSide side) =>
        new(path, ["Karpik.Engine.Sdk"], KarpikProjectKind.Runtime, side, [], []);

    private static ProjectRuntimeDescriptor CreateRuntime(string root) =>
        new(
            Path.Combine(root, "engine"),
            Path.Combine(root, "client-bundle"),
            Path.Combine(root, "server-bundle"),
            Path.Combine(root, "client-runner.exe"),
            Path.Combine(root, "server-runner.exe"));

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"KarpikEditorLifetimeTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class RecordingProcessFactory(params IMsBuildProcess[] processes)
        : IMsBuildProcessFactory
    {
        private readonly Queue<IMsBuildProcess> _processes = new(processes);
        public List<ProcessStartInfo> StartInfos { get; } = [];

        public IMsBuildProcess Start(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            return _processes.Count == 0 ? new CompletedProcess() : _processes.Dequeue();
        }
    }

    private sealed class CompletedProcess : IMsBuildProcess
    {
        public int ExitCode => 0;
        public Task<string> ReadStandardOutputAsync(int maximumCharacters, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
        public Task<string> ReadStandardErrorAsync(int maximumCharacters, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Kill(bool entireProcessTree) => throw new InvalidOperationException("already exited");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingProcess : IMsBuildProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExitCode => -1;
        public bool KillRequested { get; private set; }
        public bool ExitWaitCompleted { get; private set; }
        public bool Disposed { get; private set; }

        public Task<string> ReadStandardOutputAsync(int maximumCharacters, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
        public Task<string> ReadStandardErrorAsync(int maximumCharacters, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _exit.Task.WaitAsync(cancellationToken);
            ExitWaitCompleted = true;
        }
        public void Kill(bool entireProcessTree)
        {
            KillRequested = entireProcessTree;
            _exit.TrySetResult();
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FaultingOutputProcess : IMsBuildProcess
    {
        public int ExitCode => 0;
        public bool Disposed { get; private set; }
        public Task<string> ReadStandardOutputAsync(int maximumCharacters, CancellationToken cancellationToken) =>
            Task.FromException<string>(new IOException("read failed"));
        public Task<string> ReadStandardErrorAsync(int maximumCharacters, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Kill(bool entireProcessTree) => throw new InvalidOperationException("already exited");
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingFaultingOutputProcess : IMsBuildProcess
    {
        private readonly TaskCompletionSource _exit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExitCode => -1;
        public bool Disposed { get; private set; }
        public Task<string> ReadStandardOutputAsync(int maximumCharacters, CancellationToken cancellationToken) =>
            Task.FromException<string>(new IOException("read failed"));
        public Task<string> ReadStandardErrorAsync(int maximumCharacters, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _exit.Task.WaitAsync(cancellationToken);
        }
        public void Kill(bool entireProcessTree) => _exit.TrySetResult();
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
