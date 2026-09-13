using Karpik.Editor;
using Karpik.Engine.Core;
using Karpik.Engine.ProjectModel;
using ReactiveUI.Builder;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Karpik.Editor.Tests.Projects;

public sealed class EditorShellProjectLifecycleTests
{
    static EditorShellProjectLifecycleTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    public async Task OpenProjectAsync_ExistingSolutionIsValidatedByProjectOpenService()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"KarpikEditorShellTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string solutionPath = Path.Combine(directory, "Game.slnx");
        await File.WriteAllTextAsync(
            solutionPath,
            "<Solution />",
            TestContext.Current.CancellationToken);

        try
        {
            var opener = new RejectingProjectOpenService();
            using var viewModel = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")),
                opener);

            ProjectOpenResult result = await viewModel.OpenProjectAsync(
                solutionPath,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal(1, opener.OpenCount);
            Assert.Equal(Path.GetFullPath(solutionPath), opener.SolutionPath);
            Assert.Null(viewModel.ProjectPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenProjectAsync_ShowsMsBuildStatusWhileRuntimeEvaluationRuns()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"KarpikEditorShellTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string solutionPath = Path.Combine(directory, "Game.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution />", TestContext.Current.CancellationToken);

        try
        {
            var opener = new GatedProjectOpenService();
            using var viewModel = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")),
                opener);

            Task<ProjectOpenResult> opening = viewModel.OpenProjectAsync(
                solutionPath,
                TestContext.Current.CancellationToken);
            await opener.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            try
            {
                Assert.Equal("Проверка MSBuild…", viewModel.Status);
            }
            finally
            {
                opener.Release.SetResult();
                await opening;
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenProjectAsync_SuccessUsesCandidateOwnedSessionManager()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"KarpikEditorShellTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string solutionPath = Path.Combine(directory, "Game.slnx");
        await File.WriteAllTextAsync(
            solutionPath,
            "<Solution />",
            TestContext.Current.CancellationToken);

        try
        {
            var backendFactory = new RecordingBackendFactory();
            var lifetime = new TestEditorProjectLifetime(backendFactory);
            ActiveProjectContext candidate = CreateContext(solutionPath, lifetime);
            using var viewModel = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")),
                new SuccessfulProjectOpenService(candidate));

            ProjectOpenResult result = await viewModel.OpenProjectAsync(
                solutionPath,
                TestContext.Current.CancellationToken);
            await viewModel.StartServerCommand
                .Execute()
                .FirstAsync()
                .ToTask(TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(Path.GetFullPath(solutionPath), viewModel.ProjectPath);
            Assert.Equal([Side.Server], backendFactory.CreatedSides);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenProjectAsync_FailedReplacementClearsPublishedProjectState()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"KarpikEditorShellTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string firstPath = Path.Combine(directory, "First.slnx");
        string secondPath = Path.Combine(directory, "Second.slnx");
        await File.WriteAllTextAsync(firstPath, "<Solution />", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(secondPath, "<Solution />", TestContext.Current.CancellationToken);

        try
        {
            var first = CreateContext(
                firstPath,
                new TestEditorProjectLifetime(new RecordingBackendFactory()));
            var opener = new QueueProjectOpenService(
                ProjectOpenResult.Success(first),
                ProjectOpenResult.Failure("replacement rejected"));
            using var viewModel = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")),
                opener);

            await viewModel.OpenProjectAsync(firstPath, TestContext.Current.CancellationToken);
            ProjectOpenResult replacement = await viewModel.OpenProjectAsync(
                secondPath,
                TestContext.Current.CancellationToken);

            Assert.False(replacement.IsSuccess);
            Assert.Null(viewModel.ProjectPath);
            Assert.False(viewModel.CanStartServer);
            Assert.False(viewModel.CanBuild);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAndPublishCommands_RunThroughActiveProjectLifetime()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"KarpikEditorShellTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string solutionPath = Path.Combine(directory, "Game.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution />", TestContext.Current.CancellationToken);

        try
        {
            var lifetime = new TestEditorProjectLifetime(new RecordingBackendFactory());
            using var viewModel = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")),
                new SuccessfulProjectOpenService(CreateContext(solutionPath, lifetime)));
            await viewModel.OpenProjectAsync(solutionPath, TestContext.Current.CancellationToken);

            await viewModel.BuildProjectCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);
            await viewModel.PublishProjectCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);

            Assert.Equal(1, lifetime.BuildCount);
            Assert.Equal(1, lifetime.PublishCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SessionCommands_AreSerializedWithProjectCommands()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"KarpikEditorShellTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string solutionPath = Path.Combine(directory, "Game.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution />", TestContext.Current.CancellationToken);

        try
        {
            var backendFactory = new RecordingBackendFactory();
            var lifetime = new TestEditorProjectLifetime(backendFactory, blockBuild: true);
            using var viewModel = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")),
                new SuccessfulProjectOpenService(CreateContext(solutionPath, lifetime)));
            await viewModel.OpenProjectAsync(solutionPath, TestContext.Current.CancellationToken);

            Task build = viewModel.BuildProjectCommand.Execute().FirstAsync().ToTask();
            await lifetime.BuildStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Task startServer = viewModel.StartServerCommand.Execute().FirstAsync().ToTask();

            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Empty(backendFactory.CreatedSides);

            lifetime.ReleaseBuild();
            await build.WaitAsync(TestContext.Current.CancellationToken);
            await startServer.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal([Side.Server], backendFactory.CreatedSides);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ShutdownAsync_UsesCoordinatorFullProjectTeardown()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"KarpikEditorShellTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string solutionPath = Path.Combine(directory, "Game.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution />", TestContext.Current.CancellationToken);

        try
        {
            var events = new List<string>();
            var lifetime = new TestEditorProjectLifetime(new RecordingBackendFactory(), events);
            var viewModel = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(directory, "workspace.json")),
                new SuccessfulProjectOpenService(CreateContext(solutionPath, lifetime)));
            await viewModel.OpenProjectAsync(solutionPath, TestContext.Current.CancellationToken);

            await viewModel.ShutdownAsync();

            Assert.Equal(
                [
                    "cancel-build",
                    "stop-clients",
                    "stop-server",
                    "dispose-services",
                    "save-workspace",
                    "dispose-context"
                ],
                events);
            Assert.Null(viewModel.ProjectPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ActiveProjectContext CreateContext(
        string solutionPath,
        IActiveProjectLifetime lifetime)
    {
        string root = Path.Combine(Path.GetDirectoryName(solutionPath)!, "engine");
        return new ActiveProjectContext(
            new KarpikSolutionModel(Path.GetFullPath(solutionPath), "0.6.0-test", []),
            new ProjectRuntimeDescriptor(
                root,
                Path.Combine(root, "client-bundle"),
                Path.Combine(root, "server-bundle"),
                Path.Combine(root, "client-runner.exe"),
                Path.Combine(root, "server-runner.exe")),
            new ProjectGeneration(1),
            lifetime);
    }

    private sealed class RejectingProjectOpenService : IProjectOpenService
    {
        public int OpenCount { get; private set; }
        public string? SolutionPath { get; private set; }

        public Task<ProjectOpenResult> OpenAsync(
            string solutionPath,
            ProjectGeneration generation,
            CancellationToken cancellationToken)
        {
            OpenCount++;
            SolutionPath = solutionPath;
            return Task.FromResult(ProjectOpenResult.Failure("rejected by test"));
        }
    }

    private sealed class GatedProjectOpenService : IProjectOpenService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProjectOpenResult> OpenAsync(
            string solutionPath,
            ProjectGeneration generation,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return ProjectOpenResult.Failure("rejected by test");
        }
    }

    private sealed class SuccessfulProjectOpenService(ActiveProjectContext candidate)
        : IProjectOpenService
    {
        public Task<ProjectOpenResult> OpenAsync(
            string solutionPath,
            ProjectGeneration generation,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProjectOpenResult.Success(candidate));
    }

    private sealed class QueueProjectOpenService(params ProjectOpenResult[] results)
        : IProjectOpenService
    {
        private readonly Queue<ProjectOpenResult> _results = new(results);

        public Task<ProjectOpenResult> OpenAsync(
            string solutionPath,
            ProjectGeneration generation,
            CancellationToken cancellationToken) =>
            Task.FromResult(_results.Dequeue());
    }

    private sealed class TestEditorProjectLifetime : IEditorProjectLifetime
    {
        private readonly List<string>? _events;
        private readonly bool _blockBuild;
        private readonly TaskCompletionSource _buildRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TestEditorProjectLifetime(
            IEditorBackendFactory backendFactory,
            List<string>? events = null,
            bool blockBuild = false)
        {
            SessionManager = new EditorSessionManager(backendFactory);
            _events = events;
            _blockBuild = blockBuild;
        }

        public EditorSessionManager SessionManager { get; }
        public TaskCompletionSource BuildStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int BuildCount { get; private set; }
        public int PublishCount { get; private set; }
        public async Task BuildAsync(Action<string> output, CancellationToken cancellationToken)
        {
            BuildCount++;
            BuildStarted.TrySetResult();
            if (_blockBuild)
            {
                await _buildRelease.Task.WaitAsync(cancellationToken);
            }
        }
        public void ReleaseBuild() => _buildRelease.TrySetResult();
        public Task PublishAsync(Action<string> output, CancellationToken cancellationToken)
        {
            PublishCount++;
            return Task.CompletedTask;
        }
        public Task CancelActiveBuildAsync(CancellationToken cancellationToken) => Record("cancel-build");
        public Task StopClientsAsync(CancellationToken cancellationToken) => Record("stop-clients");
        public Task StopServerAsync(CancellationToken cancellationToken) => Record("stop-server");
        public Task DisposeProjectServicesAsync(CancellationToken cancellationToken) => Record("dispose-services");
        public Task SaveWorkspaceAsync(string solutionPath, CancellationToken cancellationToken) => Record("save-workspace");
        public ValueTask DisposeAsync()
        {
            _events?.Add("dispose-context");
            SessionManager.Dispose();
            return ValueTask.CompletedTask;
        }

        private Task Record(string name)
        {
            _events?.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBackendFactory : IEditorBackendFactory
    {
        public List<Side> CreatedSides { get; } = [];

        public IEditorBackend Create(Side side)
        {
            CreatedSides.Add(side);
            return new RecordingBackend(side);
        }
    }

    private sealed class RecordingBackend(Side side) : IEditorBackend
    {
        public Side Side { get; } = side;
        public EditorPreviewState State { get; private set; }
        public int? ProcessId => null;
        public event Action<EditorPreviewState>? StateChanged;
        public event Action<string>? OutputReceived
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            State = EditorPreviewState.Running;
            StateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            State = EditorPreviewState.Stopped;
            StateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public Task<EditorRuntimeSnapshot?> RequestSnapshotAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EditorRuntimeSnapshot?>(null);

        public void Dispose()
        {
            StateChanged = null;
        }
    }
}
