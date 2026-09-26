using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Karpik.Content.Core;
using Karpik.Engine.Core;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Karpik.Editor;

public sealed class EditorEntityViewModel
{
    public required int Id { get; init; }
    public string Title => $"Entity {Id}";
    public required IReadOnlyList<EditorComponentSnapshot> Components { get; init; }
}

[Export]
public sealed class ProjectViewModel : ReactiveObject
{
    private string? _path;
    private string _assetMessage = "Откройте проект, чтобы увидеть ассеты.";
    private string _metaMessage = "Выберите ассет, чтобы изменить его .meta.";
    private string? _contentPath;
    private AssetTreeItemViewModel? _selectedAsset;
    private AssetMetaEditorViewModel? _selectedMeta;

    public ProjectViewModel()
    {
        SaveMetaCommand = ReactiveCommand.Create(() => { SaveSelectedMeta(); });
    }

    public string Title => "Проект";
    public string Name => _path is null ? "Проект не открыт" : System.IO.Path.GetFileName(_path);
    public ObservableCollection<AssetTreeItemViewModel> Assets { get; } = [];
    public ReactiveCommand<RxVoid, RxVoid> SaveMetaCommand { get; }

    public AssetTreeItemViewModel? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAsset, value);
            LoadSelectedMeta();
        }
    }

    public AssetMetaEditorViewModel? SelectedMeta
    {
        get => _selectedMeta;
        private set => this.RaiseAndSetIfChanged(ref _selectedMeta, value);
    }

    public string MetaMessage
    {
        get => _metaMessage;
        private set => this.RaiseAndSetIfChanged(ref _metaMessage, value);
    }

    public string AssetMessage
    {
        get => _assetMessage;
        private set => this.RaiseAndSetIfChanged(ref _assetMessage, value);
    }

    public string? Path
    {
        get => _path;
        set
        {
            this.RaiseAndSetIfChanged(ref _path, value);
            this.RaisePropertyChanged(nameof(Name));
            LoadAssets();
        }
    }

    private void LoadAssets()
    {
        Assets.Clear();
        SelectedAsset = null;
        SelectedMeta = null;
        if (_path is null)
        {
            _contentPath = null;
            AssetMessage = "Откройте проект, чтобы увидеть ассеты.";
            return;
        }

        string contentPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(_path)!,
            "Content");
        _contentPath = contentPath;
        if (!Directory.Exists(contentPath))
        {
            AssetMessage = "Папка Content не найдена.";
            return;
        }

        try
        {
            EnsureMetaFiles(contentPath);
            foreach (AssetTreeItemViewModel item in LoadDirectory(contentPath))
            {
                Assets.Add(item);
            }
            AssetMessage = Assets.Count == 0 ? "Папка Content пуста." : string.Empty;
        }
        catch (IOException)
        {
            AssetMessage = "Не удалось прочитать папку Content.";
        }
        catch (UnauthorizedAccessException)
        {
            AssetMessage = "Нет доступа к папке Content.";
        }
    }

    public bool SaveSelectedMeta()
    {
        if (SelectedMeta is null)
        {
            MetaMessage = "Выберите ассет, чтобы изменить его .meta.";
            return false;
        }

        try
        {
            string candidate = SelectedMeta.ToJson();
            var diagnostics = new List<ContentDiagnostic>();
            AssetMeta parsed = AssetMeta.Parse(candidate, GetRelativeMetaPath(SelectedMeta.MetaPath), diagnostics);
            if (!StringComparer.Ordinal.Equals(parsed.AssetId.ToCanonicalString(), SelectedMeta.AssetId))
            {
                MetaMessage = "assetId нельзя изменять в редакторе.";
                return false;
            }

            string canonicalMeta = parsed.ToCanonicalMetaJson();
            string temporaryPath = SelectedMeta.MetaPath + ".tmp";
            File.WriteAllText(temporaryPath, canonicalMeta);
            File.Move(temporaryPath, SelectedMeta.MetaPath, overwrite: true);
            MetaMessage = "Сохранено.";
            LoadSelectedMeta();
            return true;
        }
        catch (JsonException ex)
        {
            MetaMessage = $"Некорректный JSON: {ex.Message}";
            return false;
        }
        catch (InvalidDataException ex)
        {
            MetaMessage = ex.Message;
            return false;
        }
        catch (IOException)
        {
            MetaMessage = "Не удалось сохранить .meta.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            MetaMessage = "Нет доступа для сохранения .meta.";
            return false;
        }
    }

    private void LoadSelectedMeta()
    {
        SelectedMeta = null;
        if (SelectedAsset?.SourcePath is not string sourcePath)
        {
            MetaMessage = "Выберите ассет, чтобы изменить его .meta.";
            return;
        }

        string metaPath = sourcePath + ".meta";
        try
        {
            var diagnostics = new List<ContentDiagnostic>();
            AssetMeta meta = AssetMeta.Parse(File.ReadAllText(metaPath), GetRelativeMetaPath(metaPath), diagnostics);
            SelectedMeta = new AssetMetaEditorViewModel(meta, metaPath);
            MetaMessage = string.Empty;
        }
        catch (IOException)
        {
            MetaMessage = "Не удалось прочитать .meta.";
        }
        catch (UnauthorizedAccessException)
        {
            MetaMessage = "Нет доступа к .meta.";
        }
        catch (InvalidDataException ex)
        {
            MetaMessage = ex.Message;
        }
    }

    private string? GetRelativeMetaPath(string metaPath) =>
        _contentPath is null ? null : System.IO.Path.GetRelativePath(_contentPath, metaPath);

    private static void EnsureMetaFiles(string contentPath)
    {
        foreach (string sourcePath in Directory.EnumerateFiles(contentPath, "*", SearchOption.AllDirectories))
        {
            if (sourcePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                || File.Exists(sourcePath + ".meta"))
            {
                continue;
            }

            string relativePath = System.IO.Path.GetRelativePath(contentPath, sourcePath);
            if (!ContentMetaTemplate.TryCreate(relativePath, "game", out string metaJson))
            {
                continue;
            }

            try
            {
                using FileStream stream = new(sourcePath + ".meta", FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(metaJson);
            }
            catch (IOException) when (File.Exists(sourcePath + ".meta"))
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static IEnumerable<AssetTreeItemViewModel> LoadDirectory(string path)
    {
        foreach (string child in Directory.EnumerateFileSystemEntries(path)
                     .OrderBy(entry => Directory.Exists(entry) ? 0 : 1)
                     .ThenBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (child.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Directory.Exists(child))
            {
                var directory = new AssetTreeItemViewModel(System.IO.Path.GetFileName(child), true);
                foreach (AssetTreeItemViewModel descendant in LoadDirectory(child))
                {
                    directory.Children.Add(descendant);
                }
                yield return directory;
            }
            else
            {
                yield return new AssetTreeItemViewModel(System.IO.Path.GetFileName(child), false, child);
            }
        }
    }
}

public sealed class AssetTreeItemViewModel(string name, bool isDirectory, string? sourcePath = null)
{
    public string Name { get; } = name;
    public bool IsDirectory { get; } = isDirectory;
    public string? SourcePath { get; } = sourcePath;
    public ObservableCollection<AssetTreeItemViewModel> Children { get; } = [];
}

public sealed class AssetMetaEditorViewModel : ReactiveObject
{
    private string _declaredType;
    private string _logicalName;
    private bool _includesClient;
    private bool _includesServer;
    private string _importSettingsJson;
    private string _dependenciesJson;

    public AssetMetaEditorViewModel(AssetMeta meta, string metaPath)
    {
        MetaPath = metaPath;
        AssetId = meta.AssetId.ToCanonicalString();
        _declaredType = meta.DeclaredType;
        _logicalName = meta.LogicalName;
        _includesClient = (meta.Targets & AssetTarget.Client) != 0;
        _includesServer = (meta.Targets & AssetTarget.Server) != 0;
        _importSettingsJson = meta.RawImportSettingsJson;
        _dependenciesJson = JsonSerializer.Serialize(meta.Dependencies.Select(dependency => dependency.ToCanonicalString()));
    }

    internal string MetaPath { get; }
    public string AssetId { get; }

    public string DeclaredType
    {
        get => _declaredType;
        set => this.RaiseAndSetIfChanged(ref _declaredType, value);
    }

    public string LogicalName
    {
        get => _logicalName;
        set => this.RaiseAndSetIfChanged(ref _logicalName, value);
    }

    public bool IncludesClient
    {
        get => _includesClient;
        set => this.RaiseAndSetIfChanged(ref _includesClient, value);
    }

    public bool IncludesServer
    {
        get => _includesServer;
        set => this.RaiseAndSetIfChanged(ref _includesServer, value);
    }

    public string ImportSettingsJson
    {
        get => _importSettingsJson;
        set => this.RaiseAndSetIfChanged(ref _importSettingsJson, value);
    }

    public string DependenciesJson
    {
        get => _dependenciesJson;
        set => this.RaiseAndSetIfChanged(ref _dependenciesJson, value);
    }

    internal string ToJson()
    {
        using JsonDocument importSettings = JsonDocument.Parse(ImportSettingsJson);
        using JsonDocument dependencies = JsonDocument.Parse(DependenciesJson);
        var targets = new List<string>(2);
        if (IncludesClient) targets.Add("Client");
        if (IncludesServer) targets.Add("Server");

        return JsonSerializer.Serialize(new
        {
            schemaVersion = AssetMeta.CurrentSchemaVersion,
            assetId = AssetId,
            declaredType = DeclaredType,
            logicalName = LogicalName,
            targets,
            importSettings = importSettings.RootElement,
            dependencies = dependencies.RootElement
        });
    }
}

[Export]
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

[Export]
public sealed class InspectorViewModel : ReactiveObject
{
    private IReadOnlyList<EditorComponentSnapshot> _components = [];
    private AssetMetaEditorViewModel? _selectedMeta;
    private string _metaMessage = "Выберите ассет, чтобы изменить его .meta.";
    private ReactiveCommand<RxVoid, RxVoid>? _saveMetaCommand;

    public string Title => "Инспектор";
    public IReadOnlyList<EditorComponentSnapshot> Components
    {
        get => _components;
        set => this.RaiseAndSetIfChanged(ref _components, value);
    }

    public AssetMetaEditorViewModel? SelectedMeta
    {
        get => _selectedMeta;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMeta, value);
            this.RaisePropertyChanged(nameof(HasSelectedMeta));
        }
    }

    public bool HasSelectedMeta => _selectedMeta is not null;

    public string MetaMessage
    {
        get => _metaMessage;
        set => this.RaiseAndSetIfChanged(ref _metaMessage, value);
    }

    public ReactiveCommand<RxVoid, RxVoid>? SaveMetaCommand
    {
        get => _saveMetaCommand;
        set => this.RaiseAndSetIfChanged(ref _saveMetaCommand, value);
    }
}

[Export]
public sealed class ConsoleViewModel : ReactiveObject
{
    public const string AllSessions = "Все сессии";
    public const string EditorSession = "Редактор";
    private readonly Queue<EditorConsoleLogEntry> _allEntries = new(2_000);
    private int _minimumLevel = 2;
    private string _selectedLevel = "Info";
    private string _selectedSession = AllSessions;

    public string Title => "Консоль";
    public ObservableCollection<string> Entries { get; } = [];
    public IReadOnlyList<EditorConsoleLogEntry> AllEntries => _allEntries.ToArray();
    public ObservableCollection<string> Sessions { get; } = [AllSessions, EditorSession];
    public IReadOnlyList<string> Levels { get; } = ["Trace", "Debug", "Info", "Warn", "Error", "Critical"];
    public ReactiveCommand<RxVoid, RxVoid> ClearCommand { get; }

    public int MinimumLevel
    {
        get => _minimumLevel;
        set
        {
            this.RaiseAndSetIfChanged(ref _minimumLevel, value);
            RefreshEntries();
        }
    }

    public string SelectedSession
    {
        get => _selectedSession;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSession, value);
            RefreshEntries();
        }
    }

    public string SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLevel, value);
            MinimumLevel = value switch
            {
                "Trace" => 0,
                "Debug" => 1,
                "Info" => 2,
                "Warn" => 3,
                "Error" => 4,
                "Critical" => 5,
                _ => 1
            };
        }
    }

    public ConsoleViewModel()
    {
        ClearCommand = ReactiveCommand.Create(Clear);
    }

    public void RegisterSession(string session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session);
        if (!Sessions.Contains(session))
        {
            Sessions.Add(session);
        }
    }

    public void Add(EditorConsoleLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        RegisterSession(entry.Session);
        if (_allEntries.Count == 2_000)
        {
            _allEntries.Dequeue();
        }

        _allEntries.Enqueue(entry);
        if (Matches(entry))
        {
            Entries.Add(Format(entry));
        }
    }

    public void Add(string line)
    {
        Add(new EditorConsoleLogEntry(DateTimeOffset.UtcNow, EditorSession, Level: 2, line));
    }

    public void Clear()
    {
        _allEntries.Clear();
        Entries.Clear();
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        foreach (EditorConsoleLogEntry entry in _allEntries.Where(Matches))
        {
            Entries.Add(Format(entry));
        }
    }

    private bool Matches(EditorConsoleLogEntry entry) =>
        entry.Level >= MinimumLevel
        && (SelectedSession == AllSessions || entry.Session == SelectedSession);

    private static string Format(EditorConsoleLogEntry entry) =>
        $"{SessionIcon(entry.Session)} {LevelIcon(entry.Level)} {entry.Message.ReplaceLineEndings(" ↵ ")}";

    private static string SessionIcon(string session) => session switch
    {
        "Сервер" => "▣",
        EditorSession => "✎",
        _ when session.StartsWith("Клиент ", StringComparison.Ordinal) => $"● {session["Клиент ".Length..]}",
        _ => session
    };

    private static string LevelIcon(int level) => level switch
    {
        0 => "·",
        1 => "◌",
        2 => "ℹ",
        3 => "⚠",
        4 => "✖",
        5 => "‼",
        _ => level.ToString()
    };
}

[Export]
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

    public ReactiveCommand<RxVoid, RxVoid> StopCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RestartCommand { get; }

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

[Export]
[Export(typeof(IActiveProjectPublisher))]
public sealed class EditorShellViewModel : ReactiveObject, IDisposable, IActiveProjectPublisher
{
    private readonly WorkspaceStore _workspaceStore;
    private readonly EditorLogArchive _logArchive;
    private EditorSessionManager? _sessionManager;
    private readonly CancellationTokenSource _lifetime = new();
    private ProjectSwitchCoordinator? _projectCoordinator;
    private SessionCommandBinding? _attachedSessionBinding;
    private Action<EditorSession>? _sessionAddedHandler;
    private Action<EditorSession?>? _selectionChangedHandler;
    private Action<EditorSession, EditorPreviewState>? _sessionStateChangedHandler;
    private Action<EditorSession, string>? _sessionOutputHandler;
    private Action<Exception>? _backgroundOperationFailedHandler;
    private string? _projectPath;
    private string _status = "Проект не открыт";
    private Task? _snapshotLoop;
    private ProjectGeneration _snapshotGeneration;
    private bool _runtimeReady;
    private bool _isProjectOpening;
    private bool _disposed;
    private bool _shutdownCompleted;

    private EditorSessionManager SessionManager => _sessionManager
        ?? throw new InvalidOperationException("No active project session manager is available.");

    public HierarchyViewModel Hierarchy { get; }
    public ProjectViewModel Project { get; }
    public InspectorViewModel Inspector { get; }
    public ConsoleViewModel Console { get; }
    public PreviewViewModel Preview { get; }
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

    public bool IsProjectOpening
    {
        get => _isProjectOpening;
        private set => this.RaiseAndSetIfChanged(ref _isProjectOpening, value);
    }

    public bool CanStartServer
    {
        get
        {
            EditorSession? server = _sessionManager?.Sessions.FirstOrDefault(session => session.Side == Side.Server);
            return _runtimeReady
                   && ProjectPath is not null
                   && server?.State is not (EditorPreviewState.Starting
                          or EditorPreviewState.Running
                          or EditorPreviewState.Stopping);
        }
    }

    public bool CanAddClient => _runtimeReady && ProjectPath is not null && _sessionManager?.CanAddClient == true;
    public bool CanStopAll => _sessionManager?.Sessions.Any(session => session.State != EditorPreviewState.Stopped) == true;
    public bool CanBuild => ProjectPath is not null;
    public bool CanPublish => ProjectPath is not null;
    public bool CanCheckRuntime => ProjectPath is not null && !_runtimeReady;

    public ReactiveCommand<RxVoid, RxVoid> StartServerCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddClientCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> StopAllCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> BuildProjectCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> PublishProjectCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CheckRuntimeCommand { get; }

    public EditorShellViewModel(WorkspaceStore workspaceStore)
        : this(workspaceStore, new EditorStartupOptions(null, null))
    {
    }

    public EditorShellViewModel(
        WorkspaceStore workspaceStore,
        EditorStartupOptions startupOptions)
        : this(
            workspaceStore,
            CreateProjectOpenService(workspaceStore),
            CreateProjectHandoffService(startupOptions))
    {
    }

    public EditorShellViewModel(
        WorkspaceStore workspaceStore,
        IProjectOpenService projectOpenService)
        : this(workspaceStore, projectOpenService, NullProjectHandoffService.Instance)
    {
    }

    public EditorShellViewModel(
        WorkspaceStore workspaceStore,
        IProjectOpenService projectOpenService,
        IProjectHandoffService handoffService)
        : this(workspaceStore, projectOpenService, handoffService,
            new HierarchyViewModel(), new ProjectViewModel(), new InspectorViewModel(),
            new ConsoleViewModel(), new PreviewViewModel(), new EditorLogArchive())
    {
    }

    public EditorShellViewModel(
        WorkspaceStore workspaceStore,
        IProjectOpenService projectOpenService,
        IProjectHandoffService handoffService,
        HierarchyViewModel hierarchy,
        ProjectViewModel project,
        InspectorViewModel inspector,
        ConsoleViewModel console,
        PreviewViewModel preview,
        EditorLogArchive logArchive)
        : this(workspaceStore, new UnavailableEditorBackendFactory(),
            hierarchy, project, inspector, console, preview, logArchive)
    {
        EditorSessionManager? placeholder = _sessionManager;
        DetachSessionManager();
        placeholder?.Dispose();
        _projectCoordinator = new ProjectSwitchCoordinator(
            projectOpenService,
            this,
            handoffService: handoffService);
        _projectCoordinator.PropertyChanged += OnProjectCoordinatorPropertyChanged;
    }

    internal EditorShellViewModel(WorkspaceStore workspaceStore, IEditorBackendFactory backendFactory)
        : this(workspaceStore, backendFactory,
            new HierarchyViewModel(), new ProjectViewModel(), new InspectorViewModel(),
            new ConsoleViewModel(), new PreviewViewModel(), new EditorLogArchive())
    {
    }

    private EditorShellViewModel(
        WorkspaceStore workspaceStore,
        IEditorBackendFactory backendFactory,
        HierarchyViewModel hierarchy,
        ProjectViewModel project,
        InspectorViewModel inspector,
        ConsoleViewModel console,
        PreviewViewModel preview,
        EditorLogArchive logArchive)
    {
        _workspaceStore = workspaceStore;
        _logArchive = logArchive;
        Hierarchy = hierarchy;
        Project = project;
        Inspector = inspector;
        Console = console;
        Preview = preview;
        Sessions = new SessionsViewModel(SelectSession);
        AttachSessionManager(new EditorSessionManager(backendFactory), default);

        Hierarchy.WhenAnyValue(x => x.SelectedEntity)
            .Subscribe(entity => Inspector.Components = entity?.Components ?? []);
        Project.WhenAnyValue(x => x.SelectedMeta)
            .Subscribe(meta => Inspector.SelectedMeta = meta);
        Project.WhenAnyValue(x => x.MetaMessage)
            .Subscribe(message => Inspector.MetaMessage = message);
        Inspector.SaveMetaCommand = Project.SaveMetaCommand;

        Project.WhenAnyValue(x => x.SelectedAsset)
            .Subscribe(asset =>
            {
                if (asset is not null)
                {
                    Hierarchy.SelectedEntity = null;
                }
            });
        Hierarchy.WhenAnyValue(x => x.SelectedEntity)
            .Subscribe(entity =>
            {
                if (entity is not null)
                {
                    Project.SelectedAsset = null;
                }
            });

        StartServerCommand = ReactiveCommand.CreateFromTask(StartServerAsync, this.WhenAnyValue(x => x.CanStartServer));
        AddClientCommand = ReactiveCommand.CreateFromTask(AddClientAsync, this.WhenAnyValue(x => x.CanAddClient));
        StopAllCommand = ReactiveCommand.CreateFromTask(StopAllAsync, this.WhenAnyValue(x => x.CanStopAll));
        BuildProjectCommand = ReactiveCommand.CreateFromTask(BuildProjectAsync, this.WhenAnyValue(x => x.CanBuild));
        PublishProjectCommand = ReactiveCommand.CreateFromTask(PublishProjectAsync, this.WhenAnyValue(x => x.CanPublish));
        CheckRuntimeCommand = ReactiveCommand.CreateFromTask(CheckRuntimeAsync);
    }

    public async Task<ProjectOpenResult> OpenProjectAsync(
        string solutionPath,
        CancellationToken cancellationToken = default,
        bool evaluateRuntime = true)
    {
        string fullPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Solution file not found", fullPath);
        }

        ProjectSwitchCoordinator coordinator = _projectCoordinator
            ?? throw new InvalidOperationException("Project opening is unavailable in this editor shell instance.");
        IsProjectOpening = true;
        Status = "Проверка MSBuild…";
        try
        {
            ProjectOpenResult result = await coordinator.SwitchAsync(fullPath, cancellationToken, evaluateRuntime);
            if (!result.IsSuccess)
            {
                Status = string.Join(Environment.NewLine, result.Diagnostics);
            }
            else if (result.Diagnostics.Count > 0)
            {
                Status = string.Join(Environment.NewLine, result.Diagnostics);
                foreach (string diagnostic in result.Diagnostics)
                {
                    ReportEditorMessage(diagnostic, level: 4);
                }
            }
            return result;
        }
        finally
        {
            IsProjectOpening = false;
        }
    }

    public async Task<EditorWorkspace> RestoreAsync(
        bool openSolution = true,
        CancellationToken cancellationToken = default)
    {
        EditorWorkspace workspace = await _workspaceStore.LoadAsync(cancellationToken);
        if (openSolution && workspace.SolutionPath is { } path && File.Exists(path))
        {
            await OpenProjectAsync(path, cancellationToken);
        }

        return workspace;
    }

    public Task PublishAsync(
        ActiveProjectContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EditorSessionManager sessionManager = context.SessionManager
            ?? throw new InvalidOperationException("The active project candidate has no editor services.");
        AttachSessionManager(sessionManager, context.Generation);
        _runtimeReady = context.IsRuntimeReady;
        ProjectPath = context.SolutionPath;
        Project.Path = context.SolutionPath;
        Status = context.IsRuntimeReady
            ? $"Открыт проект: {Path.GetFileName(context.SolutionPath)}"
            : $"Открыт проект: {Path.GetFileName(context.SolutionPath)}. Запуск недоступен: runtime не проверен.";
        ReportEditorMessage($"Открыт проект {context.SolutionPath}");
        RaiseCommandState();
        return Task.CompletedTask;
    }

    internal static IProjectOpenService CreateProjectOpenService(WorkspaceStore workspaceStore) =>
        new ProjectOpenService(
            contextFactory: new ActiveProjectContextFactory(
                (solution, runtime) => new EditorProjectLifetime(workspaceStore, solution, runtime)));

    internal static IProjectHandoffService CreateProjectHandoffService(EditorStartupOptions startupOptions)
    {
        ArgumentNullException.ThrowIfNull(startupOptions);
        if (string.IsNullOrWhiteSpace(startupOptions.HandoffPath))
        {
            return NullProjectHandoffService.Instance;
        }

        string? engineRoot = Environment.GetEnvironmentVariable("KarpikEngineRoot");
        if (string.IsNullOrWhiteSpace(engineRoot))
        {
            return new FailedProjectHandoffService(
                "The launcher did not provide KarpikEngineRoot to the editor process.");
        }
        try
        {
            return new ProjectHandoffService(startupOptions.HandoffPath, engineRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new FailedProjectHandoffService(
                $"Editor handoff configuration is invalid: {exception.Message}");
        }
    }

    private sealed class FailedProjectHandoffService(string diagnostic) : IProjectHandoffService
    {
        public ProjectHandoffResult Prepare(string solutionPath) =>
            ProjectHandoffResult.Failure(diagnostic);
    }

    private sealed class UnavailableEditorBackendFactory : IEditorBackendFactory
    {
        public IEditorBackend Create(Side side) =>
            throw new InvalidOperationException("No active project runtime is available.");
    }

    private void AttachSessionManager(
        EditorSessionManager sessionManager,
        ProjectGeneration generation)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        if (ReferenceEquals(_sessionManager, sessionManager))
        {
            return;
        }
        DetachSessionManager();
        _runtimeReady = false;
        _sessionManager = sessionManager;
        var binding = new SessionCommandBinding(sessionManager, generation);
        _attachedSessionBinding = binding;
        _sessionAddedHandler = session => OnSessionAdded(binding, session);
        _selectionChangedHandler = session => OnSelectionChanged(binding, session);
        _sessionStateChangedHandler = (session, state) =>
            OnSessionStateChanged(binding, session, state);
        _sessionOutputHandler = (session, line) => OnSessionOutput(binding, session, line);
        _backgroundOperationFailedHandler = exception =>
            OnBackgroundOperationFailed(binding, exception);
        sessionManager.SessionAdded += _sessionAddedHandler;
        sessionManager.SelectionChanged += _selectionChangedHandler;
        sessionManager.SessionStateChanged += _sessionStateChangedHandler;
        sessionManager.OutputReceived += _sessionOutputHandler;
        sessionManager.BackgroundOperationFailed += _backgroundOperationFailedHandler;
        Sessions.Items.Clear();
        Sessions.SelectFromManager(null);
    }

    private void DetachSessionManager()
    {
        if (_sessionManager is null)
        {
            return;
        }
        _sessionManager.SessionAdded -= _sessionAddedHandler;
        _sessionManager.SelectionChanged -= _selectionChangedHandler;
        _sessionManager.SessionStateChanged -= _sessionStateChangedHandler;
        _sessionManager.OutputReceived -= _sessionOutputHandler;
        _sessionManager.BackgroundOperationFailed -= _backgroundOperationFailedHandler;
        _sessionManager = null;
        _attachedSessionBinding = null;
        _sessionAddedHandler = null;
        _selectionChangedHandler = null;
        _sessionStateChangedHandler = null;
        _sessionOutputHandler = null;
        _backgroundOperationFailedHandler = null;
    }

    private void OnProjectCoordinatorPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProjectSwitchCoordinator.ActiveProject)
            && _projectCoordinator?.ActiveProject is null)
        {
            ClearPublishedProject();
        }
    }

    private void ClearPublishedProject()
    {
        DetachSessionManager();
        ProjectPath = null;
        Project.Path = null;
        Sessions.Items.Clear();
        Sessions.SelectFromManager(null);
        Hierarchy.SelectedEntity = null;
        Hierarchy.Entities.Clear();
        Inspector.Components = [];
        Preview.Message = "Проект не открыт.";
        RaiseCommandState();
    }

    public Task SaveWorkspaceAsync(
        double windowWidth,
        double windowHeight,
        double leftWidth,
        double bottomHeight,
        EditorLayoutPreset layoutPreset = EditorLayoutPreset.Unity,
        EditorUiDensity uiDensity = EditorUiDensity.Compact) =>
        _workspaceStore.SaveAsync(new EditorWorkspace
        {
            SolutionPath = ProjectPath,
            UiDensity = uiDensity,
            LayoutPreset = layoutPreset,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            LeftPanelWidth = leftWidth,
            BottomPanelHeight = bottomHeight
        });

    private async Task StartServerAsync()
    {
        try
        {
            SessionCommandBinding binding = await ExecuteSessionCommandAsync(
                static (manager, token) => manager.StartServerAsync(token));
            EnsureSnapshotLoop(binding);
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
            SessionCommandBinding binding = await ExecuteSessionCommandAsync(
                static (manager, token) => manager.AddClientAsync(token));
            EnsureSnapshotLoop(binding);
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
            await ExecuteSessionCommandAsync(
                static (manager, token) => manager.StopAllAsync(token));
        }
        catch (Exception ex)
        {
            EnqueueOutput($"Не удалось остановить сессии: {ex.Message}");
        }
    }

    private async Task BuildProjectAsync()
    {
        ProjectSwitchCoordinator? coordinator = _projectCoordinator;
        ActiveProjectContext? active = coordinator?.ActiveProject;
        if (coordinator is null || active is null)
        {
            return;
        }
        ProjectGeneration generation = active.Generation;
        try
        {
            Status = "Сборка...";
            await coordinator.ExecuteCommandAsync(
                generation,
                (context, token) => context.BuildAsync(static _ => { }, token),
                _lifetime.Token);
            if (!coordinator.TryAcceptOutput(generation))
            {
                return;
            }
            EnqueueOutput("Сборка завершена успешно");
            Status = "Сборка успешна";
        }
        catch (OperationCanceledException)
        {
            if (!coordinator.TryAcceptOutput(generation))
            {
                return;
            }
            EnqueueOutput("Сборка отменена", level: 3);
            Status = "Отменено";
        }
        catch (Exception ex)
        {
            if (!coordinator.TryAcceptOutput(generation))
            {
                return;
            }
            EnqueueOutput($"Ошибка сборки: {ex.Message}", level: 4);
            Status = "Ошибка сборки";
        }
    }

    private Task CheckRuntimeAsync() => ProjectPath is { } path
        ? OpenProjectAsync(path, _lifetime.Token, evaluateRuntime: true)
        : Task.CompletedTask;

    private async Task PublishProjectAsync()
    {
        ProjectSwitchCoordinator? coordinator = _projectCoordinator;
        ActiveProjectContext? active = coordinator?.ActiveProject;
        if (coordinator is null || active is null)
        {
            return;
        }
        ProjectGeneration generation = active.Generation;
        try
        {
            Status = "Публикация...";
            await coordinator.ExecuteCommandAsync(
                generation,
                (context, token) => context.PublishAsync(static _ => { }, token),
                _lifetime.Token);
            if (!coordinator.TryAcceptOutput(generation))
            {
                return;
            }
            EnqueueOutput("Публикация завершена");
            Status = "Публикация завершена";
        }
        catch (OperationCanceledException)
        {
            if (!coordinator.TryAcceptOutput(generation))
            {
                return;
            }
            EnqueueOutput("Публикация отменена", level: 3);
            Status = "Отменено";
        }
        catch (Exception ex)
        {
            if (!coordinator.TryAcceptOutput(generation))
            {
                return;
            }
            EnqueueOutput($"Ошибка публикации: {ex.Message}", level: 4);
            Status = "Ошибка публикации";
        }
    }

    private async Task StopSessionAsync(EditorSession session)
    {
        try
        {
            await ExecuteSessionCommandAsync(
                (manager, token) => ExecuteForOwnedSessionAsync(
                    manager,
                    session,
                    static (ownedManager, ownedSession, ownedToken) =>
                        ownedManager.StopSessionAsync(ownedSession, ownedToken),
                    token));
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
            SessionCommandBinding binding = await ExecuteSessionCommandAsync(
                (manager, token) => ExecuteForOwnedSessionAsync(
                    manager,
                    session,
                    static (ownedManager, ownedSession, ownedToken) =>
                        ownedManager.RestartSessionAsync(ownedSession, ownedToken),
                    token));
            EnsureSnapshotLoop(binding);
        }
        catch (Exception ex)
        {
            EnqueueOutput($"Не удалось перезапустить {session.Name}: {ex.Message}");
        }
    }

    private async Task PollSnapshotsAsync(
        SessionCommandBinding binding,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!IsCurrentBinding(binding))
                {
                    return;
                }

                EditorSession? requestedSession = binding.Manager.SelectedSession;
                if (requestedSession?.State != EditorPreviewState.Running)
                {
                    continue;
                }

                EditorRuntimeSnapshot? snapshot;
                try
                {
                    snapshot = await binding.Manager.RequestSelectedSnapshotAsync(
                        TimeSpan.FromMilliseconds(200),
                        cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    if (!IsCurrentBinding(binding))
                    {
                        return;
                    }
                    EnqueueOutput($"[{requestedSession.Name}] Не удалось получить ECS snapshot: {ex.Message}");
                    continue;
                }

                if (snapshot is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (IsCurrentBinding(binding)
                            && ReferenceEquals(requestedSession, binding.Manager.SelectedSession))
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

    private void EnsureSnapshotLoop(SessionCommandBinding binding)
    {
        if (_snapshotLoop is null
            || _snapshotLoop.IsCompleted
            || _snapshotGeneration != binding.Generation)
        {
            _snapshotGeneration = binding.Generation;
            _snapshotLoop = PollSnapshotsAsync(binding, _lifetime.Token);
        }
    }

    private async Task<SessionCommandBinding> ExecuteSessionCommandAsync(
        Func<EditorSessionManager, CancellationToken, Task> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProjectSwitchCoordinator coordinator = _projectCoordinator
            ?? throw new InvalidOperationException("No active project coordinator is available.");
        ActiveProjectContext active = coordinator.ActiveProject
            ?? throw new InvalidOperationException("No active project is available.");
        ProjectGeneration generation = active.Generation;
        EditorSessionManager? manager = null;
        await coordinator.ExecuteCommandAsync(
            generation,
            async (context, token) =>
            {
                manager = context.SessionManager
                    ?? throw new InvalidOperationException(
                        "The active project has no editor session services.");
                await command(manager, token);
            },
            _lifetime.Token);
        return new SessionCommandBinding(
            manager ?? throw new InvalidOperationException(
                "The project command did not acquire session services."),
            generation);
    }

    private static Task ExecuteForOwnedSessionAsync(
        EditorSessionManager manager,
        EditorSession session,
        Func<EditorSessionManager, EditorSession, CancellationToken, Task> command,
        CancellationToken cancellationToken)
    {
        if (!manager.Sessions.Contains(session))
        {
            throw new InvalidOperationException("The session belongs to a stale project generation.");
        }
        return command(manager, session, cancellationToken);
    }

    private bool IsCurrentBinding(SessionCommandBinding binding)
    {
        if (!ReferenceEquals(_sessionManager, binding.Manager)
            || _attachedSessionBinding != binding)
        {
            return false;
        }
        return _projectCoordinator is null
            ? !binding.Generation.IsValid
            : _projectCoordinator.TryAcceptOutput(binding.Generation);
    }

    private readonly record struct SessionCommandBinding(
        EditorSessionManager Manager,
        ProjectGeneration Generation);

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
        EditorSessionManager manager = SessionManager;
        if (!manager.Sessions.Contains(item.Session))
        {
            return;
        }
        manager.SelectSession(item.Session);
        ClearSnapshot(item.Session);
    }

    private void OnSessionAdded(
        SessionCommandBinding binding,
        EditorSession session) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsCurrentBinding(binding))
        {
            return;
        }
        Sessions.Items.Add(new SessionItemViewModel(session, StopSessionAsync, RestartSessionAsync));
        Console.RegisterSession(session.Name);
        RefreshSessions();
    });

    private void OnSelectionChanged(
        SessionCommandBinding binding,
        EditorSession? session) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsCurrentBinding(binding))
        {
            return;
        }
        Sessions.SelectFromManager(session);
        ClearSnapshot(session);
    });

    private void OnSessionStateChanged(
        SessionCommandBinding binding,
        EditorSession session,
        EditorPreviewState state) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsCurrentBinding(binding))
            {
                return;
            }
            RefreshSessions();
            Status = state switch
            {
                EditorPreviewState.Starting => $"{session.Name}: запускается…",
                EditorPreviewState.Running => $"{session.Name}: запущен",
                EditorPreviewState.Stopping => $"{session.Name}: останавливается…",
                EditorPreviewState.Faulted => $"{session.Name}: ошибка",
                _ => $"{session.Name}: остановлен"
            };

            if (ReferenceEquals(session, binding.Manager.SelectedSession)
                && state != EditorPreviewState.Running)
            {
                ClearSnapshot(session);
            }
        });

    private void OnSessionOutput(
        SessionCommandBinding binding,
        EditorSession session,
        string line)
    {
        if (!IsCurrentBinding(binding)
            || !EditorConsoleLogProtocol.TryParse(line, out EditorConsoleLogEvent? output))
        {
            return;
        }

        var entry = new EditorConsoleLogEntry(output!.Timestamp, session.Name, output.Level, output.Message);
        _logArchive.Append(entry);
        Dispatcher.UIThread.Post(() =>
        {
            if (IsCurrentBinding(binding))
            {
                Console.Add(entry);
            }
        });
    }

    private void OnBackgroundOperationFailed(
        SessionCommandBinding binding,
        Exception exception)
    {
        if (IsCurrentBinding(binding))
        {
            OnBackgroundOperationFailed(exception);
        }
    }

    private void OnBackgroundOperationFailed(Exception exception) =>
        EnqueueOutput($"Ошибка фоновой операции: {exception.Message}");

    internal void ReportEditorMessage(string message, int level = 2) =>
        EnqueueOutput(message, level);

    private void EnqueueOutput(string message, int level = 2)
    {
        var entry = new EditorConsoleLogEntry(DateTimeOffset.UtcNow, ConsoleViewModel.EditorSession, level, message);
        _logArchive.Append(entry);

        if (Dispatcher.UIThread.CheckAccess())
        {
            Console.Add(entry);
            return;
        }

        Dispatcher.UIThread.Post(() => Console.Add(entry));
    }

    private void RefreshSessions()
    {
        foreach (SessionItemViewModel item in Sessions.Items)
        {
            bool canRestart = item.Session.State is EditorPreviewState.Stopped or EditorPreviewState.Faulted
                              && (item.Session.Side == Side.Server || SessionManager.CanAddClient);
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

    private void RaiseCommandState()
    {
        this.RaisePropertyChanged(nameof(CanStartServer));
        this.RaisePropertyChanged(nameof(CanAddClient));
        this.RaisePropertyChanged(nameof(CanStopAll));
        this.RaisePropertyChanged(nameof(CanBuild));
        this.RaisePropertyChanged(nameof(CanPublish));
        this.RaisePropertyChanged(nameof(CanCheckRuntime));
    }

    public async Task ShutdownAsync()
    {
        if (_shutdownCompleted)
        {
            return;
        }
        _lifetime.Cancel();
        try
        {
            if (_projectCoordinator is not null)
            {
                await _projectCoordinator.DisposeAsync();
            }
            else if (_sessionManager is not null)
            {
                await _sessionManager.StopAllAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            EnqueueOutput($"Ошибка при завершении сессий: {ex.Message}");
            throw;
        }
        _shutdownCompleted = true;
        await DisposeAsyncCore();
    }

    public void Dispose()
    {
        DisposeAsyncCore().GetAwaiter().GetResult();
    }

    private async Task DisposeAsyncCore()
    {
        if (_disposed)
        {
            return;
        }

        if (!_shutdownCompleted && _projectCoordinator is not null)
        {
            _lifetime.Cancel();
            _projectCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _shutdownCompleted = true;
        }

        _disposed = true;
        _lifetime.Cancel();
        if (_projectCoordinator is not null)
        {
            _projectCoordinator.PropertyChanged -= OnProjectCoordinatorPropertyChanged;
        }
        EditorSessionManager? sessionManager = _sessionManager;
        DetachSessionManager();
        sessionManager?.Dispose();
        await _logArchive.DisposeAsync();
        _lifetime.Dispose();
    }
}
