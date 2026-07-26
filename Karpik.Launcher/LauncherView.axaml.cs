using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Karpik.Launcher.Models;
using Karpik.Launcher.ViewModels;
using ReactiveUI.Avalonia;

namespace Karpik.Launcher;

public partial class LauncherView : ReactiveUserControl<ILauncherViewModel>
{
    public LauncherView()
    {
        InitializeComponent();
    }

    private void RecentProjects_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RecentProjectsList.SelectedItem is RecentProject project)
        {
            ViewModel.OpenRecentProjectCommand.Execute(project.SolutionPath);
        }
    }
}