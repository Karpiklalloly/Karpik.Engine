using Karpik.Engine.Core;

namespace Karpik.Editor;

public sealed class EditorSessionManager : IDisposable
{
    private readonly IEditorBackendFactory _backendFactory;
    private readonly List<EditorSession> _sessions = [];
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private EditorSession? _server;
    private int _nextClientOrdinal = 1;
    private int _serverStopInProgress;
    private long _selectionVersion;
    private bool _disposed;

    public IReadOnlyList<EditorSession> Sessions => _sessions;
    public EditorSession? SelectedSession { get; private set; }
    public bool CanAddClient => _server?.State == EditorPreviewState.Running;

    public event Action<EditorSession>? SessionAdded;
    public event Action<EditorSession?>? SelectionChanged;
    public event Action<EditorSession, EditorPreviewState>? SessionStateChanged;
    public event Action<EditorSession, string>? OutputReceived;
    public event Action<Exception>? BackgroundOperationFailed;

    public EditorSessionManager(IEditorBackendFactory backendFactory)
    {
        _backendFactory = backendFactory;
    }

    public Task StartServerAsync(CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(StartServerCoreAsync, cancellationToken);

    public Task AddClientAsync(CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(AddClientCoreAsync, cancellationToken);

    public Task StopSessionAsync(
        EditorSession session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        return RunLifecycleAsync(
            token => StopSessionCoreAsync(session, token),
            cancellationToken);
    }

    public Task RestartSessionAsync(
        EditorSession session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        return RunLifecycleAsync(
            token => RestartSessionCoreAsync(session, token),
            cancellationToken);
    }

    public Task StopAllAsync(CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(StopAllCoreAsync, cancellationToken);

    public void SelectSession(EditorSession session)
    {
        ValidateSession(session);
        SetSelectedSession(session);
    }

    public async Task<EditorRuntimeSnapshot?> RequestSelectedSnapshotAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EditorSession? selected = SelectedSession;
        long selectionVersion = _selectionVersion;
        if (selected?.State != EditorPreviewState.Running)
        {
            return null;
        }

        EditorRuntimeSnapshot? snapshot = await selected.Backend.RequestSnapshotAsync(
            timeout,
            cancellationToken);
        return selectionVersion == _selectionVersion
               && ReferenceEquals(selected, SelectedSession)
            ? snapshot
            : null;
    }

    private async Task StartServerCoreAsync(CancellationToken cancellationToken)
    {
        _server ??= AddSession(Side.Server, "Сервер");
        if (_server.State != EditorPreviewState.Running)
        {
            await _server.Backend.StartAsync(cancellationToken);
        }

        SetSelectedSession(_server);
    }

    private async Task AddClientCoreAsync(CancellationToken cancellationToken)
    {
        if (!CanAddClient)
        {
            throw new InvalidOperationException("Сначала запустите сервер.");
        }

        var client = AddSession(Side.Client, $"Клиент {_nextClientOrdinal++}");
        await client.Backend.StartAsync(cancellationToken);
        SetSelectedSession(client);
    }

    private async Task StopSessionCoreAsync(
        EditorSession session,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(session, _server))
        {
            if (session.State != EditorPreviewState.Stopped)
            {
                await session.Backend.StopAsync(cancellationToken);
            }

            return;
        }

        Interlocked.Increment(ref _serverStopInProgress);
        try
        {
            List<Exception>? failures = null;
            try
            {
                await StopClientsCoreAsync(cancellationToken);
            }
            catch (AggregateException ex)
            {
                failures = [.. ex.InnerExceptions];
            }

            try
            {
                if (session.State != EditorPreviewState.Stopped)
                {
                    await session.Backend.StopAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }

            if (failures is not null)
            {
                throw new AggregateException("One or more editor sessions failed to stop.", failures);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _serverStopInProgress);
        }
    }

    private async Task RestartSessionCoreAsync(
        EditorSession session,
        CancellationToken cancellationToken)
    {
        if (ReferenceEquals(session, _server))
        {
            await StopSessionCoreAsync(session, cancellationToken);
            await session.Backend.StartAsync(cancellationToken);
            SetSelectedSession(session);
            return;
        }

        if (!CanAddClient)
        {
            throw new InvalidOperationException("Сначала запустите сервер.");
        }

        if (session.State != EditorPreviewState.Stopped)
        {
            await session.Backend.StopAsync(cancellationToken);
        }

        await session.Backend.StartAsync(cancellationToken);
        SetSelectedSession(session);
    }

    private Task StopAllCoreAsync(CancellationToken cancellationToken)
    {
        return _server is null
            ? Task.CompletedTask
            : StopSessionCoreAsync(_server, cancellationToken);
    }

    private async Task StopClientsCoreAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        foreach (EditorSession client in _sessions.Where(item => item.Side == Side.Client))
        {
            if (client.State is EditorPreviewState.Starting
                or EditorPreviewState.Running
                or EditorPreviewState.Faulted)
            {
                try
                {
                    await client.Backend.StopAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more client sessions failed to stop.", failures);
        }
    }

    private async Task RunLifecycleAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await operation(cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void ValidateSession(EditorSession session)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(session);
        if (!_sessions.Contains(session))
        {
            throw new ArgumentException("Сессия не принадлежит этому менеджеру.", nameof(session));
        }
    }

    private void SetSelectedSession(EditorSession session)
    {
        if (ReferenceEquals(SelectedSession, session))
        {
            return;
        }

        SelectedSession = session;
        _selectionVersion++;
        SelectionChanged?.Invoke(session);
    }

    private EditorSession AddSession(Side side, string name)
    {
        var session = new EditorSession(name, _backendFactory.Create(side));
        session.AttachStateHandler(state => OnSessionStateChanged(session, state));
        session.AttachOutputHandler(line => OutputReceived?.Invoke(session, line));
        _sessions.Add(session);
        SessionAdded?.Invoke(session);
        return session;
    }

    private void OnSessionStateChanged(EditorSession session, EditorPreviewState state)
    {
        SessionStateChanged?.Invoke(session, state);
        if (ReferenceEquals(session, _server)
            && Volatile.Read(ref _serverStopInProgress) == 0
            && state is EditorPreviewState.Stopped or EditorPreviewState.Faulted)
        {
            _ = StopClientsAfterServerExitAsync();
        }
    }

    private async Task StopClientsAfterServerExitAsync()
    {
        try
        {
            await RunLifecycleAsync(StopClientsCoreAsync, CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            // Editor shutdown already owns disposal.
        }
        catch (Exception ex)
        {
            BackgroundOperationFailed?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (EditorSession session in _sessions)
        {
            session.Dispose();
        }
    }
}

public sealed class EditorSession : IDisposable
{
    internal IEditorBackend Backend { get; }
    private Action<EditorPreviewState>? _stateHandler;
    private Action<string>? _outputHandler;

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public Side Side => Backend.Side;
    public EditorPreviewState State => Backend.State;
    public int? ProcessId => Backend.ProcessId;

    internal EditorSession(string name, IEditorBackend backend)
    {
        Name = name;
        Backend = backend;
    }

    internal void AttachStateHandler(Action<EditorPreviewState> handler)
    {
        _stateHandler = handler;
        Backend.StateChanged += handler;
    }

    internal void AttachOutputHandler(Action<string> handler)
    {
        _outputHandler = handler;
        Backend.OutputReceived += handler;
    }

    public void Dispose()
    {
        if (_stateHandler is not null)
        {
            Backend.StateChanged -= _stateHandler;
            _stateHandler = null;
        }

        if (_outputHandler is not null)
        {
            Backend.OutputReceived -= _outputHandler;
            _outputHandler = null;
        }

        Backend.Dispose();
    }
}
