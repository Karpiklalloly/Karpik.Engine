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
        try
        {
            layout = _layoutPreset switch
            {
                EditorLayoutPreset.Debug => _dockFactory.CreateLayout(EditorLayoutPreset.Debug),
                EditorLayoutPreset.Custom => LoadCustomLayout(),
                _ => _currentLayoutStore.Load()
                     ?? _dockFactory.CreateLayout(EditorLayoutPreset.Unity)
            };
        }
        catch (Exception ex)
        {
            _viewModel.ReportEditorMessage($"Не удалось восстановить раскладку: {ex.Message}", level: 4);
            layout = _dockFactory.CreateLayout(EditorLayoutPreset.Unity);
        }

        _dockFactory.AttachContexts(layout);
        _dockFactory.InitLayout(layout);
        DockHost.Factory = _dockFactory;
        DockHost.Layout = layout;
    }

    private IRootDock LoadCustomLayout()
    {
        try
        {
            IRootDock? layout = _customLayoutStore.Load();
            _customLayoutLoaded = layout is not null;
            if (layout is not null)
            {
                return layout;
            }

            _viewModel.ReportEditorMessage("Пользовательская раскладка не найдена. Используется Unity.", level: 4);
        }
        catch (Exception ex)
        {
            _customLayoutLoaded = false;
            _viewModel.ReportEditorMessage($"Не удалось загрузить пользовательскую раскладку: {ex.Message}", level: 4);
        }

        return _dockFactory!.CreateLayout(EditorLayoutPreset.Unity);
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
            _customLayoutStore.Save(activeLayout);
            _customLayoutLoaded = true;
            _uiDensity = density;
            _layoutPreset = preset;
            ApplyDensity(density);

            IRootDock layout = preset == EditorLayoutPreset.Custom
                ? LoadCustomLayout()
                : _dockFactory.CreateLayout(preset);
            _dockFactory.AttachContexts(layout);
            _dockFactory.InitLayout(layout);
            DockHost.Layout = layout;
            _currentLayoutStore.Save(layout);

            double left = _dockFactory.LeftDock?.Proportion ?? 300;
            double bottom = _dockFactory.BottomDock?.Proportion ?? 220;
            await _viewModel.SaveWorkspaceAsync(Width, Height, left, bottom, _layoutPreset, _uiDensity);
        }
        catch (Exception ex)
        {
            _viewModel.ReportEditorMessage($"Не удалось применить настройки редактора: {ex.Message}", level: 4);
        }
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
