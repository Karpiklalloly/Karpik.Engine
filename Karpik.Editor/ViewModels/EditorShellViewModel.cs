using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia.Threading;
using Karpik.Engine.Core;
using ReactiveUI;

namespace Karpik.Editor;

public sealed class EditorEntityViewModel
{
    public required int Id { get; init; }
    public string Title => $"Entity {Id}";
    public required IReadOnlyList<EditorComponentSnapshot> Components { get; init; }
}

public sealed class ProjectViewModel : ReactiveObject
{
    private string? _path;

    public string Title => "Проект";
    public string Name => _path is null ? "Проект не открыт" : System.IO.Path.GetFileName(_path);
    public string? Path
    {
        get => _path;
        set
        {
            this.RaiseAndSetIfChanged(ref _path, value);
            this.RaisePropertyChanged(nameof(Name));
        }
    }
}

public sealed class HierarchyViewModel : ReactiveObject
{
    private EditorEntityViewModel? _selectedEntity;

    public string Title => "Иерархия";
    public ObservableCollection<EditorEntityViewModel> Entities { get; } = [];

    public EditorEntityViewModel? SelectedEntity
    {
        get => _selectedEntity;
        set => this.RaiseAndSetIfChanged(ref _selectedEntity, value);
    }
}

public sealed class InspectorViewModel : ReactiveObject
{
    private IReadOnlyList<EditorComponentSnapshot> _components = [];

    public string Title => "Инспектор";
    public IReadOnlyList<EditorComponentSnapshot> Components
    {
        get => _components;
        set => this.RaiseAndSetIfChanged(ref _components, value);
    }
}

public sealed class ConsoleViewModel : ReactiveObject
{
    public string Title => "Консоль";
    public ObservableCollection<string> Entries { get; } = [];
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }

    public ConsoleViewModel()
    {
        ClearCommand = ReactiveCommand.Create(Clear);
    }

    public void Add(string line)
    {
        if (Entries.Count == 2_000)
        {
            Entries.RemoveAt(0);
        }

        Entries.Add(line);
    }

    public void Clear() => Entries.Clear();
}

public sealed class PreviewViewModel : ReactiveObject
{
    private string _message = "Откройте проект и запустите сервер.";

    public string Title => "Предпросмотр";
    public string Message
    {
        get => _message;
        set => this.RaiseAndSetIfChanged(ref _message, value);
    }
}

public sealed class SessionItemViewModel : ReactiveObject
{
    internal EditorSession Session { get; }
    private bool _canRestart;

    public string Name => Session.Name;
    public string StateText => Session.State switch
    {
        EditorPreviewState.Starting => "Запускается",
        EditorPreviewState.Running => "Запущен",
        EditorPreviewState.Stopping => "Останавливается",
        EditorPreviewState.Faulted => "Ошибка",
        _ => "Остановлен"
    };
    public string ProcessIdText => Session.ProcessId is { } processId ? $"PID {processId}" : "PID —";
    public bool CanStop => Session.State is EditorPreviewState.Starting
        or EditorPreviewState.Running
        or EditorPreviewState.Faulted;
    public bool CanRestart
    {
        get => _canRestart;
        private set => this.RaiseAndSetIfChanged(ref _canRestart, value);
    }

    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> RestartCommand { get; }

    internal SessionItemViewModel(
        EditorSession session,
        Func<EditorSession, Task> stop,
        Func<EditorSession, Task> restart)
    {
        Session = session;
        StopCommand = ReactiveCommand.CreateFromTask(() => stop(Session));
        RestartCommand = ReactiveCommand.CreateFromTask(() => restart(Session));
    }

    internal void Refresh(bool canRestart)
    {
        CanRestart = canRestart;
        this.RaisePropertyChanged(nameof(StateText));
        this.RaisePropertyChanged(nameof(ProcessIdText));
        this.RaisePropertyChanged(nameof(CanStop));
    }
}

public sealed class SessionsViewModel : ReactiveObject
{
    private readonly Action<SessionItemViewModel> _select;
    private SessionItemViewModel? _selectedItem;
    private bool _syncingSelection;

    public string Title => "Сессии";
    public ObservableCollection<SessionItemViewModel> Items { get; } = [];
    public SessionItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedItem, value);
            if (value is null || _syncingSelection)
            {
                return;
            }

            _select(value);
        }
    }

    internal SessionsViewModel(Action<SessionItemViewModel> select) => _select = select;

    internal SessionItemViewModel? Find(EditorSession session) =>
        Items.FirstOrDefault(item => item.Session.Id == session.Id);

    internal void SelectFromManager(EditorSession? session)
    {
        _syncingSelection = true;
        try
        {
            SelectedItem = session is null ? null : Find(session);
        }
        finally
        {
            _syncingSelection = false;
        }
    }
}

public sealed class EditorShellViewModel : ReactiveObject, IDisposable
{
    private readonly WorkspaceStore _workspaceStore;
    private readonly EditorSessionManager _sessionManager;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _pendingOutputGate = new();
    private readonly Queue<string> _pendingOutput = new(2_000);
    private string? _projectPath;
    private string _status = "Проект не открыт";
    private Task? _snapshotLoop;
    private bool _outputDrainScheduled;
    private bool _disposed;

    public HierarchyViewModel Hierarchy { get; } = new();
    public ProjectViewModel Project { get; } = new();
    public InspectorViewModel Inspector { get; } = new();
    public ConsoleViewModel Console { get; } = new();
    public PreviewViewModel Preview { get; } = new();
    public SessionsViewModel Sessions { get; }

    public string? ProjectPath
    {
        get => _projectPath;
        private set => this.RaiseAndSetIfChanged(ref _projectPath, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public bool CanStartServer
    {
        get
        {
            EditorSession? server = _sessionManager.Sessions.FirstOrDefault(session => session.Side == Side.Server);
            return ProjectPath is not null
                   && server?.State is not (EditorPreviewState.Starting
                       or EditorPreviewState.Running
                       or EditorPreviewState.Stopping);
        }
    }

    public bool CanAddClient => ProjectPath is not null && _sessionManager.CanAddClient;
    public bool CanStopAll => _sessionManager.Sessions.Any(session => session.State != EditorPreviewState.Stopped);

    public ReactiveCommand<Unit, Unit> StartServerCommand { get; }
    public ReactiveCommand<Unit, Unit> AddClientCommand { get; }
    public ReactiveCommand<Unit, Unit> StopAllCommand { get; }

    public EditorShellViewModel(WorkspaceStore workspaceStore)
        : this(
            workspaceStore,
            new EditorPreviewBackendFactory(new RuntimeBundleResolver(AppContext.BaseDirectory)))
    {
    }

    internal EditorShellViewModel(WorkspaceStore workspaceStore, IEditorBackendFactory backendFactory)
    {
        _workspaceStore = workspaceStore;
        _sessionManager = new EditorSessionManager(backendFactory);
        Sessions = new SessionsViewModel(SelectSession);

        _sessionManager.SessionAdded += OnSessionAdded;
        _sessionManager.SelectionChanged += OnSelectionChanged;
        _sessionManager.SessionStateChanged += OnSessionStateChanged;
        _sessionManager.OutputReceived += OnSessionOutput;
        _sessionManager.BackgroundOperationFailed += OnBackgroundOperationFailed;

        Hierarchy.WhenAnyValue(x => x.SelectedEntity)
            .Subscribe(entity => Inspector.Components = entity?.Components ?? []);

        StartServerCommand = ReactiveCommand.CreateFromTask(StartServerAsync);
        AddClientCommand = ReactiveCommand.CreateFromTask(AddClientAsync);
        StopAllCommand = ReactiveCommand.CreateFromTask(StopAllAsync);
    }

    public async Task<EditorWorkspace> RestoreAsync(CancellationToken cancellationToken = default)
    {
        EditorWorkspace workspace = await _workspaceStore.LoadAsync(cancellationToken);
        if (workspace.ProjectPath is { } path && Directory.Exists(path))
        {
            OpenProject(path);
        }

        return workspace;
    }

    public void OpenProject(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        ProjectPath = fullPath;
        Project.Path = fullPath;
        Status = $"Открыт проект: {Path.GetFileName(fullPath)}";
        Console.Add($"[{DateTime.Now:HH:mm:ss}] Открыт проект {fullPath}");
        RaiseCommandState();
    }

    public Task SaveWorkspaceAsync(double windowWidth, double windowHeight, double leftWidth, double bottomHeight) =>
        _workspaceStore.SaveAsync(new EditorWorkspace
        {
            ProjectPath = ProjectPath,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            LeftPanelWidth = leftWidth,
            BottomPanelHeight = bottomHeight
        });

    private async Task StartServerAsync()
    {
        try
        {
            await _sessionManager.StartServerAsync(_lifetime.Token);
            EnsureSnapshotLoop();
        }
        catch (Exception ex)
        {
            EnqueueOutput($"Не удалось запустить сервер: {ex.Message}");
        }
    }

    private async Task AddClientAsync()
    {
        try
        {
            await _sessionManager.AddClientAsync(_lifetime.Token);
            EnsureSnapshotLoop();
        }
        catch (Exception ex)
        {
            EnqueueOutput($"Не удалось запустить клиент: {ex.Message}");
        }
    }

    private async Task StopAllAsync()
    {
        try
        {
            await _sessionManager.StopAllAsync(_lifetime.Token);
        }
        catch (Exception ex)
        {
            EnqueueOutput($"Не удалось остановить сессии: {ex.Message}");
        }
    }

    private async Task StopSessionAsync(EditorSession session)
    {
        try
        {
            await _sessionManager.StopSessionAsync(session, _lifetime.Token);
        }
        catch (Exception ex)
        {
            EnqueueOutput($"Не удалось остановить {session.Name}: {ex.Message}");
        }
    }

    private async Task RestartSessionAsync(EditorSession session)
    {
        try
        {
            await _sessionManager.RestartSessionAsync(session, _lifetime.Token);
        }
        catch (Exception ex)
        {
            EnqueueOutput($"Не удалось перезапустить {session.Name}: {ex.Message}");
        }
    }

    private async Task PollSnapshotsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                EditorSession? requestedSession = _sessionManager.SelectedSession;
                if (requestedSession?.State != EditorPreviewState.Running)
                {
                    continue;
                }

                EditorRuntimeSnapshot? snapshot;
                try
                {
                    snapshot = await _sessionManager.RequestSelectedSnapshotAsync(
                        TimeSpan.FromMilliseconds(200),
                        cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    EnqueueOutput($"[{requestedSession.Name}] Не удалось получить ECS snapshot: {ex.Message}");
                    continue;
                }

                if (snapshot is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (ReferenceEquals(requestedSession, _sessionManager.SelectedSession))
                        {
                            ApplySnapshot(snapshot);
                        }
                    });
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void EnsureSnapshotLoop()
    {
        if (_snapshotLoop is null || _snapshotLoop.IsCompleted)
        {
            _snapshotLoop = PollSnapshotsAsync(_lifetime.Token);
        }
    }

    private void ApplySnapshot(EditorRuntimeSnapshot snapshot)
    {
        Preview.Message = snapshot.IsTruncated
            ? $"Показано {snapshot.Entities.Length} из {snapshot.TotalEntityCount} сущностей (snapshot ограничен)."
            : $"Сущностей: {snapshot.Entities.Length}";
        int? selectedId = Hierarchy.SelectedEntity?.Id;
        Hierarchy.Entities.Clear();
        foreach (EditorEntitySnapshot entity in snapshot.Entities)
        {
            Hierarchy.Entities.Add(new EditorEntityViewModel
            {
                Id = entity.EntityId,
                Components = entity.Components
            });
        }

        Hierarchy.SelectedEntity = selectedId is null
            ? null
            : Hierarchy.Entities.FirstOrDefault(entity => entity.Id == selectedId);
    }

    private void SelectSession(SessionItemViewModel item)
    {
        _sessionManager.SelectSession(item.Session);
        ClearSnapshot(item.Session);
    }

    private void OnSessionAdded(EditorSession session) => Dispatcher.UIThread.Post(() =>
    {
        Sessions.Items.Add(new SessionItemViewModel(session, StopSessionAsync, RestartSessionAsync));
        RefreshSessions();
    });

    private void OnSelectionChanged(EditorSession? session) => Dispatcher.UIThread.Post(() =>
    {
        Sessions.SelectFromManager(session);
        ClearSnapshot(session);
    });

    private void OnSessionStateChanged(EditorSession session, EditorPreviewState state) =>
        Dispatcher.UIThread.Post(() =>
        {
            RefreshSessions();
            Status = state switch
            {
                EditorPreviewState.Starting => $"{session.Name}: запускается…",
                EditorPreviewState.Running => $"{session.Name}: запущен",
                EditorPreviewState.Stopping => $"{session.Name}: останавливается…",
                EditorPreviewState.Faulted => $"{session.Name}: ошибка",
                _ => $"{session.Name}: остановлен"
            };

            if (ReferenceEquals(session, _sessionManager.SelectedSession)
                && state != EditorPreviewState.Running)
            {
                ClearSnapshot(session);
            }
        });

    private void OnSessionOutput(EditorSession session, string line) =>
        EnqueueOutput($"[{session.Name}] {line}");

    private void OnBackgroundOperationFailed(Exception exception) =>
        EnqueueOutput($"Ошибка фоновой операции: {exception.Message}");

    private void EnqueueOutput(string line)
    {
        lock (_pendingOutputGate)
        {
            if (_pendingOutput.Count == 2_000)
            {
                _pendingOutput.Dequeue();
            }

            _pendingOutput.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (_outputDrainScheduled)
            {
                return;
            }

            _outputDrainScheduled = true;
        }

        Dispatcher.UIThread.Post(DrainOutput);
    }

    private void RefreshSessions()
    {
        foreach (SessionItemViewModel item in Sessions.Items)
        {
            bool canRestart = item.Session.State is EditorPreviewState.Stopped or EditorPreviewState.Faulted
                              && (item.Session.Side == Side.Server || _sessionManager.CanAddClient);
            item.Refresh(canRestart);
        }

        RaiseCommandState();
    }

    private void ClearSnapshot(EditorSession? session)
    {
        Hierarchy.SelectedEntity = null;
        Hierarchy.Entities.Clear();
        Inspector.Components = [];
        Preview.Message = session is null
            ? "Сессия не выбрана."
            : $"Выбрана сессия «{session.Name}». Ожидание ECS snapshot…";
    }

    private void DrainOutput()
    {
        while (true)
        {
            string line;
            lock (_pendingOutputGate)
            {
                if (_pendingOutput.Count == 0)
                {
                    _outputDrainScheduled = false;
                    return;
                }

                line = _pendingOutput.Dequeue();
            }

            Console.Add(line);
        }
    }

    private void RaiseCommandState()
    {
        this.RaisePropertyChanged(nameof(CanStartServer));
        this.RaisePropertyChanged(nameof(CanAddClient));
        this.RaisePropertyChanged(nameof(CanStopAll));
    }

    public async Task ShutdownAsync()
    {
        _lifetime.Cancel();
        try
        {
            await _sessionManager.StopAllAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            EnqueueOutput($"Ошибка при завершении сессий: {ex.Message}");
        }

        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _sessionManager.SessionAdded -= OnSessionAdded;
        _sessionManager.SelectionChanged -= OnSelectionChanged;
        _sessionManager.SessionStateChanged -= OnSessionStateChanged;
        _sessionManager.OutputReceived -= OnSessionOutput;
        _sessionManager.BackgroundOperationFailed -= OnBackgroundOperationFailed;
        _sessionManager.Dispose();
        _lifetime.Dispose();
    }
}
