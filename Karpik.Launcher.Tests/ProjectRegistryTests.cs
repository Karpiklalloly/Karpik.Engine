using Karpik.Launcher.Models;
using Karpik.Launcher.Services;
using Xunit;

namespace Karpik.Launcher.Tests;

public sealed class ProjectRegistryTests
{
    [Fact]
    public void AddDeduplicatesAProjectAndPersistsTheLatestTimestampOutsideTheGame()
    {
        using var workspace = new TestWorkspace();
        string localRoot = Path.Combine(workspace.RootPath, "local");
        string solution = workspace.CreateGame("Game", "sdk-a");
        var registry = new ProjectRegistry(localRoot);
        var first = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var second = first.AddDays(1);

        registry.Add(solution, first);
        registry.Add(solution, second);
        IReadOnlyList<RecentProject> projects = registry.Load();

        RecentProject recent = Assert.Single(projects);
        Assert.Equal(Path.GetFullPath(solution), recent.SolutionPath);
        Assert.Equal(second, recent.LastOpenedUtc);
        Assert.StartsWith(Path.GetFullPath(localRoot), registry.RegistryPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetDirectoryName(solution)!, registry.RegistryPath, StringComparison.OrdinalIgnoreCase);
    }
}
