using Karpik.Launcher.Services;
using Xunit;

namespace Karpik.Launcher.Tests;

public sealed class ProjectTemplateCatalogReaderTests
{
    [Fact]
    public void RejectsMalformedCatalogWithoutThrowing()
    {
        using var workspace = new TestWorkspace();
        string installation = workspace.CreateInstallation(Path.Combine(workspace.RootPath, "local"), "engine", "0.6.0", "0.6.0-local");
        File.WriteAllText(Path.Combine(installation, "sdk", "templates.json"), "[]");

        TemplateCatalogResult result = new ProjectTemplateCatalogReader().Read(installation);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Templates);
    }

    [Fact]
    public void ReadsTemplatesFromTheSelectedPayloadSdkDirectory()
    {
        using var workspace = new TestWorkspace();
        string installation = workspace.CreateInstallation(Path.Combine(workspace.RootPath, "local"), "engine", "0.6.0", "0.6.0-local");
        string sdk = Path.Combine(installation, "sdk");
        File.WriteAllText(Path.Combine(sdk, "Karpik.Engine.Templates.0.6.0-local.nupkg"), "template");
        File.WriteAllText(Path.Combine(sdk, "templates.json"), """
            { "templates": [{ "id": "game", "name": "Game", "description": "Test", "shortName": "game", "packageFile": "Karpik.Engine.Templates.0.6.0-local.nupkg" }] }
            """);

        TemplateCatalogResult result = new ProjectTemplateCatalogReader().Read(installation);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("game", Assert.Single(result.Templates).ShortName);
    }
}
