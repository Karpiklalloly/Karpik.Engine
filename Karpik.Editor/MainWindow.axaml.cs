using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Karpik.Engine.Tooling;

namespace Karpik.Editor;

public sealed partial class MainWindow : Window
{
    private readonly EditorShellViewModel _viewModel;
    private readonly DockLayoutStore _currentLayoutStore;
    private readonly DockLayoutStore _customLayoutStore;
    private readonly EditorStartupOptions _startupOptions;
    private EditorDockFactory? _dockFactory;
    private EditorSettingsWindow? _settingsWindow;
    private EditorUiDensity _uiDensity = EditorUiDensity.Compact;
    private EditorLayoutPreset _layoutPreset = EditorLayoutPreset.Unity;
    private bool _customLayoutLoaded;
    private bool _closeConfirmed;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(EditorStartupOptions? startupOptions)
    {
        _startupOptions = startupOptions ?? new EditorStartupOptions(null, null);
        InitializeComponent();
        _viewModel = new EditorShellViewModel(WorkspaceStore.CreateDefault(), _startupOptions);
        _currentLayoutStore = DockLayoutStore.CreateCurrent();
        _customLayoutStore = DockLayoutStore.CreateCustom();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public EditorShellViewModel ViewModel => _viewModel;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        EditorWorkspace workspace = await _viewModel.RestoreAsync(
            openSolution: string.IsNullOrWhiteSpace(_startupOptions.SolutionPath));
        if (!string.IsNullOrWhiteSpace(_startupOptions.SolutionPath))
        {
            if (!await TryOpenProjectAsync(_startupOptions.SolutionPath))
            {
                return;
            }
        }
        Width = Math.Max(MinWidth, workspace.WindowWidth);
        Height = Math.Max(MinHeight, workspace.WindowHeight);

        _dockFactory = new EditorDockFactory(_viewModel, workspace);
        _uiDensity = workspace.UiDensity;
        _layoutPreset = workspace.LayoutPreset;
        ApplyDensity(_uiDensity);
        IRootDock layout;
        EditorLayoutPreset fallbackPreset = _layoutPreset == EditorLayoutPreset.Debug
            ? EditorLayoutPreset.Debug
            : EditorLayoutPreset.Unity;
        try
        {
            layout = _layoutPreset switch
            {
                EditorLayoutPreset.Debug => LoadCurrentLayout(EditorLayoutPreset.Debug),
                EditorLayoutPreset.Custom => LoadCustomLayout(),
                _ => LoadCurrentLayout(EditorLayoutPreset.Unity)
            };
        }
        catch (Exception ex)
        {
            if (_layoutPreset == EditorLayoutPreset.Custom)
            {
                _layoutPreset = EditorLayoutPreset.Unity;
                _customLayoutLoaded = false;
            }
            _viewModel.ReportEditorMessage($"Не удалось восстановить раскладку: {ex.Message}", level: 4);
            layout = _dockFactory.CreateLayout(fallbackPreset);
        }

        layout = InitializeLayout(layout, fallbackPreset);
        DockHost.Factory = _dockFactory;
        DockHost.Layout = layout;
    }

    private IRootDock LoadCurrentLayout(EditorLayoutPreset fallbackPreset)
    {
        IRootDock? layout = _currentLayoutStore.Load();
        if (layout is not null && EditorDockFactory.IsValidLayout(layout))
        {
            return layout;
        }

        if (layout is not null)
        {
            _viewModel.ReportEditorMessage("Текущая раскладка повреждена. Используется резервная схема.", level: 4);
        }

        return _dockFactory!.CreateLayout(fallbackPreset);
    }

    private IRootDock LoadCustomLayout()
    {
        try
        {
            IRootDock? layout = _customLayoutStore.Load();
            if (layout is not null && EditorDockFactory.IsValidLayout(layout))
            {
                _customLayoutLoaded = true;
                return layout;
            }

            _customLayoutLoaded = false;
            _layoutPreset = EditorLayoutPreset.Unity;
            if (layout is not null)
            {
                _viewModel.ReportEditorMessage("Пользовательская раскладка повреждена. Используется Unity.", level: 4);
            }
            else
            {
                _viewModel.ReportEditorMessage("Пользовательская раскладка не найдена. Используется Unity.", level: 4);
            }
        }
        catch (Exception ex)
        {
            _customLayoutLoaded = false;
            _layoutPreset = EditorLayoutPreset.Unity;
            _viewModel.ReportEditorMessage($"Не удалось загрузить пользовательскую раскладку: {ex.Message}", level: 4);
        }

        return _dockFactory!.CreateLayout(EditorLayoutPreset.Unity);
    }

    private IRootDock InitializeLayout(IRootDock layout, EditorLayoutPreset fallbackPreset)
    {
        try
        {
            if (!EditorDockFactory.IsValidLayout(layout))
            {
                throw new InvalidDataException("The saved editor layout is missing required dockables.");
            }

            _dockFactory!.AttachContexts(layout);
            _dockFactory.InitLayout(layout);
            return layout;
        }
        catch (Exception ex)
        {
            if (_layoutPreset == EditorLayoutPreset.Custom)
            {
                _layoutPreset = EditorLayoutPreset.Unity;
                _customLayoutLoaded = false;
            }
            _viewModel.ReportEditorMessage($"Не удалось восстановить раскладку: {ex.Message}", level: 4);
            IRootDock fallback = _dockFactory!.CreateLayout(fallbackPreset);
            _dockFactory.AttachContexts(fallback);
            _dockFactory.InitLayout(fallback);
            return fallback;
        }
    }

    public void OpenEditorSettings()
    {
        if (_settingsWindow is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }
            existing.Activate();
            return;
        }

        _settingsWindow = new EditorSettingsWindow(_uiDensity, _layoutPreset, ApplyEditorSettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    private void EditorSettings_OnClick(object? sender, RoutedEventArgs e) => OpenEditorSettings();

    public async void ApplyEditorSettings(EditorUiDensity density, EditorLayoutPreset preset)
    {
        if (_dockFactory is null || DockHost.Layout is not IRootDock activeLayout)
        {
            return;
        }

        try
        {
            bool samePreset = preset == _layoutPreset;
            if (samePreset)
            {
                _uiDensity = density;
                ApplyDensity(density);
                _currentLayoutStore.Save(activeLayout);
                if (_layoutPreset == EditorLayoutPreset.Custom && _customLayoutLoaded)
                {
                    _customLayoutStore.Save(activeLayout);
                }
                await SaveWorkspaceStateAsync();
                return;
            }

            if (_layoutPreset != EditorLayoutPreset.Custom && !_customLayoutStore.Exists())
            {
                _customLayoutStore.Save(activeLayout);
                _customLayoutLoaded = true;
            }
            else if (_layoutPreset == EditorLayoutPreset.Custom && _customLayoutLoaded)
            {
                _customLayoutStore.Save(activeLayout);
            }
            _uiDensity = density;
            _layoutPreset = preset;
            ApplyDensity(density);

            IRootDock layout = preset == EditorLayoutPreset.Custom
                ? LoadCustomLayout()
                : _dockFactory.CreateLayout(preset);
            layout = InitializeLayout(
                layout,
                preset == EditorLayoutPreset.Debug
                    ? EditorLayoutPreset.Debug
                    : EditorLayoutPreset.Unity);
            DockHost.Layout = layout;
            _currentLayoutStore.Save(layout);

            await SaveWorkspaceStateAsync();
        }
        catch (Exception ex)
        {
            _viewModel.ReportEditorMessage($"Не удалось применить настройки редактора: {ex.Message}", level: 4);
        }
    }

    private Task SaveWorkspaceStateAsync()
    {
        double left = _dockFactory?.LeftDock?.Proportion ?? 300;
        double bottom = _dockFactory?.BottomDock?.Proportion ?? 220;
        return _viewModel.SaveWorkspaceAsync(Width, Height, left, bottom, _layoutPreset, _uiDensity);
    }

    private void ApplyDensity(EditorUiDensity density)
    {
        Classes.Remove("density-compact");
        Classes.Remove("density-ultra-compact");
        Classes.Remove("density-large");
        Classes.Add(density switch
        {
            EditorUiDensity.UltraCompact => "density-ultra-compact",
            EditorUiDensity.Large => "density-large",
            _ => "density-compact"
        });
    }

    private async void OpenProject_OnClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Открыть проект KarpikEngine",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("KarpikEngine Solution")
                    {
                        Patterns = ["*.slnx"]
                    }
                ]
            });
        if (files.Count == 0)
        {
            return;
        }

        string? path = files[0].TryGetLocalPath();
        if (path is not null)
        {
            await TryOpenProjectAsync(path);
        }
    }

    private async void SaveWorkspace_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            double left = _dockFactory?.LeftDock?.Proportion ?? 300;
            double bottom = _dockFactory?.BottomDock?.Proportion ?? 220;
            if (DockHost.Layout is IRootDock layout)
            {
                _currentLayoutStore.Save(layout);
                if (_layoutPreset == EditorLayoutPreset.Custom && _customLayoutLoaded)
                {
                    _customLayoutStore.Save(layout);
                }
            }

            await _viewModel.SaveWorkspaceAsync(Width, Height, left, bottom, _layoutPreset, _uiDensity);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportEditorMessage($"Не удалось сохранить рабочую область: {ex.Message}", level: 4);
        }
    }

    private async void SaveWorkspaceAs_OnClick(object? sender, RoutedEventArgs e)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Save Karpik Editor workspace",
                SuggestedFileName = "workspace.json",
                DefaultExtension = "json",
                FileTypeChoices =
                [
                    new FilePickerFileType("Karpik Editor Workspace")
                    {
                        Patterns = ["*.json"]
                    }
                ]
            });
        string? path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        try
        {
            double left = _dockFactory?.LeftDock?.Proportion ?? 300;
            double bottom = _dockFactory?.BottomDock?.Proportion ?? 220;
            await new WorkspaceStore(path).SaveAsync(new EditorWorkspace
            {
                SolutionPath = _viewModel.ProjectPath,
                UiDensity = _uiDensity,
                LayoutPreset = _layoutPreset,
                WindowWidth = Width,
                WindowHeight = Height,
                LeftPanelWidth = left,
                BottomPanelHeight = bottom
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportEditorMessage($"Не удалось сохранить рабочую область как файл: {ex.Message}", level: 4);
        }
    }

    private async Task<bool> TryOpenProjectAsync(string solutionPath)
    {
        try
        {
            ProjectOpenResult result = await _viewModel.OpenProjectAsync(solutionPath);
            if (result.RequiresEditorHandoff)
            {
                await ExitForHandoffAsync();
                return false;
            }
            if (!result.IsSuccess)
            {
                _viewModel.ReportEditorMessage(string.Join(Environment.NewLine, result.Diagnostics), level: 4);
            }
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            _viewModel.ReportEditorMessage($"Не удалось открыть проект: {exception.Message}", level: 4);
            return true;
        }
    }

    private async Task ExitForHandoffAsync()
    {
        await _viewModel.ShutdownAsync();
        _closeConfirmed = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(EditorExitCodes.HandoffRequested);
            return;
        }
        Close();
    }

    private void Exit_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void ConsoleLog_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox listBox
            || e.Key != Key.C
            || (e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        await TryCopyConsoleMessageAsync(listBox.SelectedItem);
    }

    private void ConsoleMessage_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control
            || e.GetCurrentPoint(control).Properties.PointerUpdateKind
            != PointerUpdateKind.RightButtonPressed)
        {
            return;
        }

        if (control.FindAncestorOfType<ListBoxItem>() is { } item)
        {
            item.IsSelected = true;
        }
    }

    private async void CopyConsoleMessage_OnClick(object? sender, RoutedEventArgs e)
    {
        object? message = (sender as MenuItem)?.CommandParameter;
        await TryCopyConsoleMessageAsync(message);
    }

    private async Task TryCopyConsoleMessageAsync(object? selectedItem)
    {
        Func<string, Task>? writeTextAsync = Clipboard is { } clipboard
            ? clipboard.SetTextAsync
            : null;

        await ConsoleMessageCopy.TryCopyAsync(selectedItem, writeTextAsync);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        e.Cancel = true;
        double left = _dockFactory?.LeftDock?.Proportion ?? 300;
        double bottom = _dockFactory?.BottomDock?.Proportion ?? 220;
        if (DockHost.Layout is IRootDock layout)
        {
            try
            {
                _currentLayoutStore.Save(layout);
                if (_layoutPreset == EditorLayoutPreset.Custom && _customLayoutLoaded)
                {
                    _customLayoutStore.Save(layout);
                }
            }
            catch (Exception ex)
            {
                _viewModel.ReportEditorMessage($"Не удалось сохранить раскладку: {ex.Message}", level: 4);
            }
        }
        try
        {
            await _viewModel.SaveWorkspaceAsync(Width, Height, left, bottom, _layoutPreset, _uiDensity);
            await _viewModel.ShutdownAsync();
        }
        catch (Exception exception)
        {
            _viewModel.ReportEditorMessage($"Не удалось безопасно закрыть проект: {exception.Message}", level: 4);
            return;
        }
        _closeConfirmed = true;
        Close();
    }
}
