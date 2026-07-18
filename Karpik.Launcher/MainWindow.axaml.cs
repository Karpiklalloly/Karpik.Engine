using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Karpik.Launcher.Models;
using Karpik.Launcher.ViewModels;

namespace Karpik.Launcher;

public sealed partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly LauncherViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new LauncherViewModel();
        DataContext = _viewModel;
        Closing += (_, _) => _lifetime.Cancel();
    }

    private async void OpenProject_OnClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Open Karpik project",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Karpik solution") { Patterns = ["*.slnx"] }
                ]
            });
        string? solutionPath = files.SingleOrDefault()?.TryGetLocalPath();
        if (solutionPath is not null)
        {
            await LaunchAsync(solutionPath);
        }
    }

    private async void OpenRecent_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is string solutionPath)
        {
            await LaunchAsync(solutionPath);
        }
    }

    private async void RecentProjects_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RecentProjectsList.SelectedItem is RecentProject project)
        {
            await LaunchAsync(project.SolutionPath);
        }
    }

    private async Task LaunchAsync(string solutionPath)
    {
        try
        {
            await _viewModel.LaunchAsync(solutionPath, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // LauncherViewModel publishes the actionable diagnostic through Status.
        }
    }
}
