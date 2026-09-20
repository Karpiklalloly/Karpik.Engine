using Karpik.Engine.Core;
using Karpik.Engine.ProjectModel;

namespace Karpik.Editor;

public sealed class EditorProjectLifetime : IEditorProjectLifetime
{
    private readonly WorkspaceStore _workspaceStore;
    private readonly KarpikSolutionModel _solution;
    private readonly EditorProjectCommandRunner _commands;
    private EditorSessionManager? _sessionManager;

    public EditorProjectLifetime(
        WorkspaceStore workspaceStore,
        KarpikSolutionModel solution,
        ProjectRuntimeDescriptor runtime,
        IMsBuildProcessFactory? processFactory = null)
    {
        _workspaceStore = workspaceStore;
        _solution = solution;
        _commands = new EditorProjectCommandRunner(processFactory);
        _sessionManager = new EditorSessionManager(
            new EditorPreviewBackendFactory(new ProjectRuntimeResolver(runtime)));
    }

    public EditorSessionManager SessionManager => _sessionManager
        ?? throw new ObjectDisposedException(nameof(EditorProjectLifetime));

    public Task CancelActiveBuildAsync(CancellationToken cancellationToken)
    {
        return _commands.CancelActiveAsync(cancellationToken);
    }

    public Task BuildAsync(Action<string> output, CancellationToken cancellationToken) =>
        _commands.RunAsync(
            Path.GetDirectoryName(_solution.SolutionPath)!,
            ["build", _solution.SolutionPath, "-nr:false"],
            output,
            cancellationToken);

    public async Task PublishAsync(Action<string> output, CancellationToken cancellationToken)
    {
        KarpikProjectDescriptor[] projects = _solution.Projects
            .Where(project => project.Kind == KarpikProjectKind.Runtime
                              && project.Side is KarpikProjectSide.Client or KarpikProjectSide.Server)
            .OrderBy(project => project.Side)
            .ToArray();
        foreach (KarpikProjectDescriptor project in projects)
        {
            await _commands.RunAsync(
                Path.GetDirectoryName(project.ProjectPath)!,
                ["publish", project.ProjectPath, "-nr:false"],
                output,
                cancellationToken);
        }
    }

    public async Task StopClientsAsync(CancellationToken cancellationToken)
    {
        if (_sessionManager is not null)
        {
            await _sessionManager.StopAllClientsAsync(cancellationToken);
        }
    }

    public async Task StopServerAsync(CancellationToken cancellationToken)
    {
        if (_sessionManager is not null)
        {
            await _sessionManager.StopServerAsync(cancellationToken);
        }
    }

    public async Task DisposeProjectServicesAsync(CancellationToken cancellationToken)
    {
        if (_sessionManager is not null)
        {
            await _sessionManager.StopAllAsync(cancellationToken);
            _sessionManager.Dispose();
            _sessionManager = null;
        }
    }

    public async Task SaveWorkspaceAsync(string solutionPath, CancellationToken cancellationToken)
    {
        EditorWorkspace current = await _workspaceStore.LoadAsync(cancellationToken);
        await _workspaceStore.SaveAsync(new EditorWorkspace
        {
            SolutionPath = solutionPath,
            UiDensity = current.UiDensity,
            LayoutPreset = current.LayoutPreset,
            WindowWidth = current.WindowWidth,
            WindowHeight = current.WindowHeight,
            LeftPanelWidth = current.LeftPanelWidth,
            BottomPanelHeight = current.BottomPanelHeight
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _commands.DisposeAsync();
        if (_sessionManager is not null)
        {
            _sessionManager.Dispose();
            _sessionManager = null;
        }
    }
}
