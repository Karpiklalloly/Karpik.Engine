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
    private readonly DockLayoutStore _layoutStore;
    private readonly EditorStartupOptions _startupOptions;
    private EditorDockFactory? _dockFactory;
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
        _layoutStore = DockLayoutStore.CreateDefault();
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
        IRootDock layout;
        try
        {
            layout = _layoutStore.Load() ?? _dockFactory.CreateLayout();
        }
        catch (Exception ex)
        {
            _viewModel.Console.Add($"Не удалось восстановить раскладку: {ex.Message}");
            layout = _dockFactory.CreateLayout();
        }

        _dockFactory.AttachContexts(layout);
        _dockFactory.InitLayout(layout);
        DockHost.Factory = _dockFactory;
        DockHost.Layout = layout;
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
                _viewModel.Console.Add(string.Join(Environment.NewLine, result.Diagnostics));
            }
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            _viewModel.Console.Add($"Не удалось открыть проект: {exception.Message}");
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
                _layoutStore.Save(layout);
            }
            catch (Exception ex)
            {
                _viewModel.Console.Add($"Не удалось сохранить раскладку: {ex.Message}");
            }
        }
        try
        {
            await _viewModel.SaveWorkspaceAsync(Width, Height, left, bottom);
            await _viewModel.ShutdownAsync();
        }
        catch (Exception exception)
        {
            _viewModel.Console.Add($"Не удалось безопасно закрыть проект: {exception.Message}");
            return;
        }
        _closeConfirmed = true;
        Close();
    }
}
