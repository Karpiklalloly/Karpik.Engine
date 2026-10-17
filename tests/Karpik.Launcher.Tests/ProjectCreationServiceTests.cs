using Karpik.Launcher.Models;
using Karpik.Launcher.Services;
using Xunit;

namespace Karpik.Launcher.Tests;

public sealed class ProjectCreationServiceTests
{
    [Fact]
    public async Task RejectsAnExistingTargetBeforeInvokingDotnet()
    {
        using var workspace = new TestWorkspace();
        string target = Path.Combine(workspace.RootPath, "Existing");
        File.WriteAllText(target, "reserved");
        string installation = Path.Combine(workspace.RootPath, "installation");
        Directory.CreateDirectory(Path.Combine(installation, "sdk"));
        File.WriteAllText(Path.Combine(installation, "sdk", "template.nupkg"), "template");

        ProjectCreationResult result = await new ProjectCreationService().CreateAsync(
            installation, "0.6.0", new ProjectTemplate("game", "Game", "Test", "game", "template.nupkg"),
            workspace.RootPath, "Existing", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Project directory already exists.", result.Message);
    }
}
