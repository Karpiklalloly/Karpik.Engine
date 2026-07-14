using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;

namespace Karpik.Editor;

public sealed partial class MainWindow : Window
{
    private readonly EditorShellViewModel _viewModel;
    private readonly DockLayoutStore _layoutStore;
    private EditorDockFactory? _dockFactory;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new EditorShellViewModel(WorkspaceStore.CreateDefault());
        _layoutStore = DockLayoutStore.CreateDefault();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        EditorWorkspace workspace = await _viewModel.RestoreAsync();
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
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Открыть проект KarpikEngine",
                AllowMultiple = false
            });
        if (folders.Count == 0)
        {
            return;
        }

        string? path = folders[0].TryGetLocalPath();
        if (path is not null)
        {
            _viewModel.OpenProject(path);
        }
    }

    private void Exit_OnClick(object? sender, RoutedEventArgs e) => Close();

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
        await _viewModel.SaveWorkspaceAsync(Width, Height, left, bottom);
        await _viewModel.ShutdownAsync();
        _closeConfirmed = true;
        Close();
    }
}
