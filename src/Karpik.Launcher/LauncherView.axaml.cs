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

    private async void CreateProject_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is null || Avalonia.Controls.TopLevel.GetTopLevel(this) is not Avalonia.Controls.Window parent) return;
        IReadOnlyList<Karpik.Engine.Tooling.InstalledEngineInstallation> installations = new Karpik.Engine.Tooling.EngineInstallationResolver().ListInstalled();
        if (installations.Count == 0) return;
        CreateProjectDialogResult? result = await new CreateProjectWindow(installations).ShowDialog<CreateProjectDialogResult?>(parent);
        if (result is null) return;
        ViewModel.RegisterCreatedProject(result.SolutionPath);
        if (result.OpenAfterCreation) await ViewModel.LaunchAsync(result.SolutionPath, CancellationToken.None);
    }

    private async void RemoveProject_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Avalonia.Controls.Button { CommandParameter: RecentProject project } ||
            Avalonia.Controls.TopLevel.GetTopLevel(this) is not Avalonia.Controls.Window parent) return;
        DeleteProjectDialogResult? result = await new DeleteProjectWindow(project).ShowDialog<DeleteProjectDialogResult?>(parent);
        if (result is not null) ViewModel.RemoveRecentProject(project.SolutionPath, result.DeleteSources);
    }
}
