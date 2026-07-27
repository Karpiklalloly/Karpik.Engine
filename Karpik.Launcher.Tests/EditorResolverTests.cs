using Karpik.Engine.Tooling;
using Karpik.Launcher.Services;
using Xunit;

namespace Karpik.Launcher.Tests;

public sealed class EditorResolverTests
{
    [Fact]
    public void CreateInstallationCreatesAValidEngineInstallation()
    {
        using var workspace = new TestWorkspace();
        string localRoot = Path.Combine(workspace.RootPath, "local");
        string installation = workspace.CreateInstallation(localRoot, "engine", "1.0.0", "sdk");

        EngineInstallationValidationResult result = new EngineInstallationValidator().Validate(installation);

        Assert.True(result.IsValid, result.Message);
    }

    [Fact]
    public void ResolveSelectsTheEditorFromTheExactSdkInstallation()
    {
        using var workspace = new TestWorkspace();
        string localRoot = Path.Combine(workspace.RootPath, "local");
        workspace.CreateInstallation(localRoot, "engine-a", "1.0.0", "sdk-a");
        string expectedRoot = workspace.CreateInstallation(localRoot, "engine-b", "2.0.0", "sdk-b");
        string solution = workspace.CreateGame("Game", "sdk-b");
        var resolver = new EditorResolver(
            new EngineInstallationResolver(localApplicationDataRoot: localRoot));

        EditorResolutionResult result = resolver.Resolve(solution);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("sdk-b", result.Descriptor!.SdkVersion);
        Assert.Equal(Path.GetFullPath(expectedRoot), result.Descriptor.InstallationRoot);
        Assert.Equal(Path.Combine(expectedRoot, "editor", "Karpik.Editor.dll"), result.Descriptor.EditorAssemblyPath);
    }

    [Fact]
    public void ResolveSurfacesMissingAndCorruptInstallationFailures()
    {
        using var workspace = new TestWorkspace();
        string localRoot = Path.Combine(workspace.RootPath, "local");
        string solution = workspace.CreateGame("Game", "sdk-missing");
        var resolver = new EditorResolver(
            new EngineInstallationResolver(localApplicationDataRoot: localRoot));

        Assert.Equal(EditorResolutionCode.InstallationFailure, resolver.Resolve(solution).Code);

        string corrupt = workspace.CreateInstallation(localRoot, "corrupt", "1.0.0", "sdk-missing");
        File.Delete(Path.Combine(corrupt, ".complete"));
        Assert.Equal(EditorResolutionCode.InstallationFailure, resolver.Resolve(solution).Code);
    }
}
