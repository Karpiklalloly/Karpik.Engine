using Karpik.Launcher.Services;
using Karpik.Launcher.ViewModels;
using Xunit;

namespace Karpik.Launcher.Tests;

public sealed class LauncherViewModelTests
{
    [Fact]
    public void RemoveRecentProjectKeepsSourcesWhenRequested()
    {
        using var workspace = new TestWorkspace();
        string solution = workspace.CreateGame("Game", "sdk-a");
        var registry = new ProjectRegistry(Path.Combine(workspace.RootPath, "local"));
        registry.Add(solution);
        var viewModel = new LauncherViewModel(registry, new RecordingHost(), null, null);

        bool removed = viewModel.RemoveRecentProject(solution, deleteSources: false);

        Assert.True(removed);
        Assert.Empty(viewModel.RecentProjects);
        Assert.True(File.Exists(solution));
    }

    [Fact]
    public void RemoveRecentProjectDeletesSourcesWhenRequested()
    {
        using var workspace = new TestWorkspace();
        string solution = workspace.CreateGame("Game", "sdk-a");
        string projectDirectory = Path.GetDirectoryName(solution)!;
        var registry = new ProjectRegistry(Path.Combine(workspace.RootPath, "local"));
        registry.Add(solution);
        var viewModel = new LauncherViewModel(registry, new RecordingHost(), null, null);

        bool removed = viewModel.RemoveRecentProject(solution, deleteSources: true);

        Assert.True(removed);
        Assert.Empty(viewModel.RecentProjects);
        Assert.False(Directory.Exists(projectDirectory));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[null]")]
    public void ConstructorSurvivesACorruptRecentProjectCacheAndShowsADiagnostic(string json)
    {
        using var workspace = new TestWorkspace();
        var registry = new ProjectRegistry(Path.Combine(workspace.RootPath, "local"));
        Directory.CreateDirectory(Path.GetDirectoryName(registry.RegistryPath)!);
        File.WriteAllText(registry.RegistryPath, json);

        var viewModel = new LauncherViewModel(registry, new RecordingHost(), null, null);

        Assert.Empty(viewModel.RecentProjects);
        Assert.Contains("recent-project", viewModel.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchAsyncPersistsTheProjectAndPublishesTheHostResult()
    {
        using var workspace = new TestWorkspace();
        string localRoot = Path.Combine(workspace.RootPath, "local");
        string solution = workspace.CreateGame("Game", "sdk-a");
        var host = new RecordingHost();
        var viewModel = new LauncherViewModel(new ProjectRegistry(localRoot), host, null, null);

        EditorHostResult result = await viewModel.LaunchAsync(
            solution,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(solution), host.SolutionPath);
        Assert.Single(viewModel.RecentProjects);
        Assert.Equal(Path.GetFullPath(solution), viewModel.RecentProjects[0].SolutionPath);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(result.Message, viewModel.Status);
    }

    private sealed class RecordingHost : IEditorProcessHost
    {
        public string? SolutionPath { get; private set; }

        public Task<EditorHostResult> RunAsync(
            string solutionPath,
            CancellationToken cancellationToken = default)
        {
            SolutionPath = solutionPath;
            return Task.FromResult(new EditorHostResult(
                true,
                EditorHostCode.Success,
                "closed",
                Path.GetFullPath(solutionPath),
                0));
        }
    }
}
