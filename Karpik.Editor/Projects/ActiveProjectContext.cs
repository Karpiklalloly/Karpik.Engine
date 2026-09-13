using Karpik.Engine.Core;
using Karpik.Engine.ProjectModel;

namespace Karpik.Editor;

public readonly record struct ProjectGeneration(long Value)
{
    public bool IsValid => Value > 0;
}

public interface IActiveProjectLifetime : IAsyncDisposable
{
    Task CancelActiveBuildAsync(CancellationToken cancellationToken);
    Task StopClientsAsync(CancellationToken cancellationToken);
    Task StopServerAsync(CancellationToken cancellationToken);
    Task DisposeProjectServicesAsync(CancellationToken cancellationToken);
    Task SaveWorkspaceAsync(string solutionPath, CancellationToken cancellationToken);
}

public interface IEditorProjectLifetime : IActiveProjectLifetime
{
    EditorSessionManager SessionManager { get; }
    Task BuildAsync(Action<string> output, CancellationToken cancellationToken);
    Task PublishAsync(Action<string> output, CancellationToken cancellationToken);
}

public sealed class NullActiveProjectLifetime : IActiveProjectLifetime
{
    public static NullActiveProjectLifetime Instance { get; } = new();

    private NullActiveProjectLifetime()
    {
    }

    public Task CancelActiveBuildAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopClientsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopServerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DisposeProjectServicesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SaveWorkspaceAsync(string solutionPath, CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ActiveProjectContext : IAsyncDisposable
{
    private readonly IActiveProjectLifetime _lifetime;
    private readonly SemaphoreSlim _disposeGate = new(1, 1);
    private int _servicesDisposed;
    private int _disposed;

    public ActiveProjectContext(
        KarpikSolutionModel solution,
        ProjectRuntimeDescriptor runtime,
        ProjectGeneration generation,
        IActiveProjectLifetime lifetime,
        bool isRuntimeReady = true)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(lifetime);
        if (!generation.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), "Project generation must be positive.");
        }

        Solution = solution with { SolutionPath = NormalizeSolutionPath(solution.SolutionPath) };
        Runtime = NormalizeRuntime(runtime);
        Generation = generation;
        IsRuntimeReady = isRuntimeReady;
        _lifetime = lifetime;
    }

    public KarpikSolutionModel Solution { get; }
    public string SolutionPath => Solution.SolutionPath;
    public ProjectRuntimeDescriptor Runtime { get; }
    public ProjectGeneration Generation { get; }
    public bool IsRuntimeReady { get; }
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal EditorSessionManager? SessionManager =>
        (_lifetime as IEditorProjectLifetime)?.SessionManager;

    internal Task BuildAsync(Action<string> output, CancellationToken cancellationToken) =>
        GetEditorLifetime().BuildAsync(output, cancellationToken);

    internal Task PublishAsync(Action<string> output, CancellationToken cancellationToken) =>
        GetEditorLifetime().PublishAsync(output, cancellationToken);

    internal Task CancelActiveBuildAsync(CancellationToken cancellationToken) =>
        _lifetime.CancelActiveBuildAsync(cancellationToken);

    internal Task StopClientsAsync(CancellationToken cancellationToken) =>
        _lifetime.StopClientsAsync(cancellationToken);

    internal Task StopServerAsync(CancellationToken cancellationToken) =>
        _lifetime.StopServerAsync(cancellationToken);

    internal async Task DisposeProjectServicesAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _servicesDisposed) != 0)
        {
            return;
        }

        await _lifetime.DisposeProjectServicesAsync(cancellationToken);
        Volatile.Write(ref _servicesDisposed, 1);
    }

    internal Task SaveWorkspaceAsync(CancellationToken cancellationToken) =>
        _lifetime.SaveWorkspaceAsync(SolutionPath, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _disposeGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }
            await _lifetime.DisposeAsync();
            Volatile.Write(ref _disposed, 1);
        }
        finally
        {
            _disposeGate.Release();
        }
    }

    private static string NormalizeSolutionPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".slnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Active project identity must be an absolute .slnx path.", nameof(path));
        }
        return fullPath;
    }

    private IEditorProjectLifetime GetEditorLifetime() =>
        _lifetime as IEditorProjectLifetime
        ?? throw new InvalidOperationException("The active context has no editor project services.");

    private static ProjectRuntimeDescriptor NormalizeRuntime(ProjectRuntimeDescriptor runtime) =>
        runtime with
        {
            EngineRoot = NormalizeAbsolute(runtime.EngineRoot, nameof(runtime.EngineRoot)),
            ClientBundlePath = NormalizeAbsolute(runtime.ClientBundlePath, nameof(runtime.ClientBundlePath)),
            ServerBundlePath = NormalizeAbsolute(runtime.ServerBundlePath, nameof(runtime.ServerBundlePath)),
            ClientRunnerPath = NormalizeAbsolute(runtime.ClientRunnerPath, nameof(runtime.ClientRunnerPath)),
            ServerRunnerPath = NormalizeAbsolute(runtime.ServerRunnerPath, nameof(runtime.ServerRunnerPath))
        };

    private static string NormalizeAbsolute(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Project runtime paths must be absolute.", parameterName);
        }
        return Path.GetFullPath(path);
    }
}

public interface IActiveProjectContextFactory
{
    ActiveProjectContext Create(
        KarpikSolutionModel solution,
        ProjectRuntimeDescriptor runtime,
        ProjectGeneration generation,
        bool isRuntimeReady = true);
}

public sealed class ActiveProjectContextFactory : IActiveProjectContextFactory
{
    private readonly Func<KarpikSolutionModel, ProjectRuntimeDescriptor, IActiveProjectLifetime> _lifetimeFactory;

    public ActiveProjectContextFactory(
        Func<KarpikSolutionModel, ProjectRuntimeDescriptor, IActiveProjectLifetime>? lifetimeFactory = null)
    {
        _lifetimeFactory = lifetimeFactory
                           ?? ((_, _) => NullActiveProjectLifetime.Instance);
    }

    public ActiveProjectContext Create(
        KarpikSolutionModel solution,
        ProjectRuntimeDescriptor runtime,
        ProjectGeneration generation,
        bool isRuntimeReady = true) =>
        new(solution, runtime, generation, _lifetimeFactory(solution, runtime), isRuntimeReady);
}
