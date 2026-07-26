using Avalonia.Input;
using Karpik.Launcher.Models;
using Karpik.Launcher.ViewModels;
using ReactiveUI.Avalonia;

namespace Karpik.Launcher;

public partial class LauncherView : ReactiveUserControl<LauncherViewModel>
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