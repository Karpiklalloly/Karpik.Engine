using Karpik.Editor;
using Karpik.Engine.Core;
using Karpik.Engine.ProjectModel;
using ReactiveUI.Builder;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorCommandPresentationTests
{
    static EditorCommandPresentationTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    public async Task ShellCommands_ForwardPresentationState()
    {
        var lifetime = new TestProjectLifetime();
        using var shell = new EditorShellViewModel(
            new WorkspaceStore(Path.Combine(Path.GetTempPath(), $"KarpikEditorTests-{Guid.NewGuid():N}.json")),
            new UnavailableEditorBackendFactory());

        AssertCommandState(shell);

        await shell.PublishAsync(CreateContext(lifetime), TestContext.Current.CancellationToken);
        AssertCommandState(shell);

        await shell.StartServerCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);
        AssertCommandState(shell);

        await shell.StopAllCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);
        AssertCommandState(shell);
    }

    private static void AssertCommandState(EditorShellViewModel shell)
    {
        Assert.Equal(shell.CanStartServer, shell.StartServerCommand.CanExecute(null));
        Assert.Equal(shell.CanAddClient, shell.AddClientCommand.CanExecute(null));
        Assert.Equal(shell.CanStopAll, shell.StopAllCommand.CanExecute(null));
        Assert.Equal(shell.CanBuild, shell.BuildProjectCommand.CanExecute(null));
        Assert.Equal(shell.CanPublish, shell.PublishProjectCommand.CanExecute(null));
    }

    private static ActiveProjectContext CreateContext(IActiveProjectLifetime lifetime)
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KarpikEditorCommandPresentation"));
        return new ActiveProjectContext(
            new KarpikSolutionModel(Path.Combine(root, "Game.slnx"), "0.6.0-test", []),
            new ProjectRuntimeDescriptor(
                root,
                Path.Combine(root, "client-bundle"),
                Path.Combine(root, "server-bundle"),
                Path.Combine(root, "client-runner.exe"),
                Path.Combine(root, "server-runner.exe")),
            new ProjectGeneration(1),
            lifetime);
    }

    private sealed class TestProjectLifetime : IEditorProjectLifetime
    {
        public EditorSessionManager SessionManager { get; } = new(new TestBackendFactory());

        public Task BuildAsync(Action<string> output, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishAsync(Action<string> output, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CancelActiveBuildAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopClientsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopServerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisposeProjectServicesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveWorkspaceAsync(string solutionPath, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            SessionManager.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestBackendFactory : IEditorBackendFactory
    {
        public IEditorBackend Create(Side side) => new TestBackend(side);
    }

    private sealed class TestBackend(Side side) : IEditorBackend
    {
        public Side Side { get; } = side;
        public EditorPreviewState State { get; private set; }
        public int? ProcessId => null;
        public event Action<EditorPreviewState>? StateChanged;
        public event Action<string>? OutputReceived { add { } remove { } }

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

        public void Dispose() => StateChanged = null;
    }
}
