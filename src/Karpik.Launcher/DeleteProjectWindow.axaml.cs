using Avalonia.Controls;
using Avalonia.Interactivity;
using Karpik.Launcher.Models;

namespace Karpik.Launcher;

public sealed record DeleteProjectDialogResult(bool DeleteSources);

public partial class DeleteProjectWindow : Window
{
    public DeleteProjectWindow()
    {
        InitializeComponent();
    }

    public DeleteProjectWindow(RecentProject project)
        : this()
    {
        ProjectPath.Text = project.SolutionPath;
    }

    private void Delete(object? sender, RoutedEventArgs e) => Close(new DeleteProjectDialogResult(DeleteSourcesBox.IsChecked == true));
    private void Cancel(object? sender, RoutedEventArgs e) => Close();
}
