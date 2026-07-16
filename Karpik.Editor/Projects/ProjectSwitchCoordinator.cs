namespace Karpik.Editor;

public interface IActiveProjectPublisher
{
    Task PublishAsync(ActiveProjectContext context, CancellationToken cancellationToken);
}

public sealed class NullActiveProjectPublisher : IActiveProjectPublisher
{
    public static NullActiveProjectPublisher Instance { get; } = new();

    private NullActiveProjectPublisher()
    {
    }

    public Task PublishAsync(ActiveProjectContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class ProjectSwitchCoordinator : IAsyncDisposable
{
    private readonly IProjectOpenService _projectOpenService;
    private readonly IActiveProjectPublisher _publisher;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private ActiveProjectContext? _ownedContext;
    private ActiveProjectContext? _activeProject;
    private long _nextGeneration;
    private int _switchInProgress;
    private int _disposed;
    private bool _commandsEnabled;

    public ProjectSwitchCoordinator(
        IProjectOpenService projectOpenService,
        IActiveProjectPublisher? publisher = null,
        ActiveProjectContext? initialContext = null)
    {
        ArgumentNullException.ThrowIfNull(projectOpenService);
        _projectOpenService = projectOpenService;
        _publisher = publisher ?? NullActiveProjectPublisher.Instance;
        _ownedContext = initialContext;
        _activeProject = initialContext;
        _commandsEnabled = initialContext is not null;
        _nextGeneration = initialContext?.Generation.Value ?? 0;
    }

    public ActiveProjectContext? ActiveProject
    {
        get
        {
            lock (_stateGate)
            {
                return _activeProject;
            }
        }
    }

    public bool CommandsEnabled
    {
        get
        {
            lock (_stateGate)
            {
                return _commandsEnabled;
            }
        }
    }

    public async Task<ProjectOpenResult> SwitchAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.CompareExchange(ref _switchInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("A project switch is already in progress.");
        }

        ActiveProjectContext? candidate = null;
        bool commandGateAcquired = false;
        try
        {
            await _commandGate.WaitAsync(cancellationToken);
            commandGateAcquired = true;
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ActiveProjectContext? previous;
            lock (_stateGate)
            {
                _commandsEnabled = false;
                previous = _ownedContext;
                _activeProject = null;
            }

            if (previous is not null)
            {
                await TeardownAsync(previous, cancellationToken);
                lock (_stateGate)
                {
                    if (ReferenceEquals(_ownedContext, previous))
                    {
                        _ownedContext = null;
                    }
                }
            }

            var generation = new ProjectGeneration(Interlocked.Increment(ref _nextGeneration));
            ProjectOpenResult openResult = await _projectOpenService.OpenAsync(
                solutionPath,
                generation,
                cancellationToken);
            if (!openResult.IsSuccess)
            {
                return openResult;
            }

            candidate = openResult.Candidate
                        ?? throw new InvalidOperationException(
                            "Successful project open did not return a candidate context.");
            if (candidate.Generation != generation ||
                !PathComparer.Equals(
                    candidate.SolutionPath,
                    Path.GetFullPath(solutionPath)))
            {
                throw new InvalidOperationException(
                    "Project candidate identity or generation does not match the switch request.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            await _publisher.PublishAsync(candidate, cancellationToken);

            lock (_stateGate)
            {
                _ownedContext = candidate;
                _activeProject = candidate;
                _commandsEnabled = true;
            }
            candidate = null;
            return openResult;
        }
        finally
        {
            try
            {
                if (candidate is not null)
                {
                    await candidate.DisposeAsync();
                }
            }
            finally
            {
                if (commandGateAcquired)
                {
                    _commandGate.Release();
                }
                Volatile.Write(ref _switchInProgress, 0);
            }
        }
    }

    public bool IsCurrent(ProjectGeneration generation)
    {
        lock (_stateGate)
        {
            return _commandsEnabled
                   && _activeProject is { } active
                   && active.Generation == generation
                   && !active.IsDisposed;
        }
    }

    public bool TryAcceptOutput(ProjectGeneration generation) => IsCurrent(generation);

    public async Task ExecuteCommandAsync(
        ProjectGeneration generation,
        Func<ActiveProjectContext, CancellationToken, Task> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _switchInProgress) != 0)
        {
            throw new InvalidOperationException("Project commands are blocked during a switch.");
        }

        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            ActiveProjectContext context;
            lock (_stateGate)
            {
                if (!_commandsEnabled ||
                    _activeProject is not { } active ||
                    active.Generation != generation ||
                    active.IsDisposed)
                {
                    throw new InvalidOperationException(
                        "The command targets no active project or a stale project generation.");
                }
                context = active;
            }
            await command(context, cancellationToken);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private static async Task TeardownAsync(
        ActiveProjectContext context,
        CancellationToken cancellationToken)
    {
        await context.CancelActiveBuildAsync(cancellationToken);
        await context.StopClientsAsync(cancellationToken);
        await context.StopServerAsync(cancellationToken);
        await context.DisposeProjectServicesAsync(cancellationToken);
        await context.SaveWorkspaceAsync(cancellationToken);
        await context.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _commandGate.WaitAsync();
        try
        {
            ActiveProjectContext? context;
            lock (_stateGate)
            {
                _commandsEnabled = false;
                _activeProject = null;
                context = _ownedContext;
                _ownedContext = null;
            }
            if (context is not null)
            {
                await context.DisposeAsync();
            }
        }
        finally
        {
            _commandGate.Release();
            _commandGate.Dispose();
        }
    }

    private static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
