using Karpik.Editor;
using Karpik.Engine.Tooling;
using Xunit;

namespace Karpik.Editor.Tests.Projects;

public sealed class ProjectHandoffServiceTests
{
    [Fact]
    public void PrepareWritesARequestOnlyWhenTheTargetSdkDiffersFromTheCurrentEditor()
    {
        using var workspace = new HandoffWorkspace();
        string engineRoot = workspace.CreateInstallation("sdk-a");
        string same = workspace.CreateGame("Same", "sdk-a");
        string different = workspace.CreateGame("Different", "sdk-b");
        string handoffPath = Path.Combine(workspace.RootPath, "handoff.json");
        var service = new ProjectHandoffService(handoffPath, engineRoot);

        ProjectHandoffResult sameResult = service.Prepare(same);
        ProjectHandoffResult differentResult = service.Prepare(different);

        Assert.Equal(ProjectHandoffCode.NotRequired, sameResult.Code);
        Assert.Equal(ProjectHandoffCode.Requested, differentResult.Code);
        Assert.Equal(Path.GetFullPath(different), EditorHandoffRequest.Read(handoffPath).SolutionPath);
    }

    [Fact]
    public void PrepareReturnsFailureForAnInvalidCurrentInstallation()
    {
        using var workspace = new HandoffWorkspace();
        string engineRoot = workspace.CreateInstallation("sdk-a");
        File.Delete(Path.Combine(engineRoot, ".complete"));
        string solution = workspace.CreateGame("Game", "sdk-b");
        var service = new ProjectHandoffService(
            Path.Combine(workspace.RootPath, "handoff.json"),
            engineRoot);

        ProjectHandoffResult result = service.Prepare(solution);

        Assert.Equal(ProjectHandoffCode.Failure, result.Code);
        Assert.Contains("installation", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class HandoffWorkspace : IDisposable
    {
        public HandoffWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "karpik-editor-handoff-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateGame(string name, string sdkVersion)
        {
            string root = Path.Combine(RootPath, name);
            Directory.CreateDirectory(root);
            string solution = Path.Combine(root, name + ".slnx");
            File.WriteAllText(solution, "<Solution />");
            File.WriteAllText(Path.Combine(root, "global.json"), $$"""
                { "msbuild-sdks": { "Karpik.Engine.Sdk": "{{sdkVersion}}" } }
                """);
            return solution;
        }

        public string CreateInstallation(string sdkVersion)
        {
            string root = Path.Combine(RootPath, "engine");
            Directory.CreateDirectory(Path.Combine(root, "editor"));
            Directory.CreateDirectory(Path.Combine(root, "sdk"));
            Directory.CreateDirectory(Path.Combine(root, "runners", "client"));
            Directory.CreateDirectory(Path.Combine(root, "runners", "server"));
            Directory.CreateDirectory(Path.Combine(root, "modules", "Module"));
            Directory.CreateDirectory(Path.Combine(root, "native"));
            File.WriteAllText(Path.Combine(root, "editor", "Karpik.Editor.dll"), "editor");
            File.WriteAllText(Path.Combine(root, "sdk", "Karpik.Engine.Sdk.nupkg"), "sdk");
            File.WriteAllText(Path.Combine(root, "runners", "client", "Karpik.Engine.Core.Runner.dll"), "client");
            File.WriteAllText(Path.Combine(root, "runners", "server", "Karpik.Engine.Core.Runner.dll"), "server");
            File.WriteAllText(Path.Combine(root, "modules", "Module", "Module.dll"), "module");
            var manifest = new EngineInstallationManifest
            {
                EngineVersion = "1.0.0",
                MsBuildSdkVersion = sdkVersion,
                EditorVersion = "1.0.0",
                RuntimeProtocolVersion = EngineInstallationManifest.CurrentRuntimeProtocolVersion,
                LayoutVersion = EngineInstallationManifest.CurrentLayoutVersion,
                ContentHash = EngineContentHash.Compute(root)
            };
            File.WriteAllText(Path.Combine(root, "engine-installation.json"), manifest.ToJson());
            File.WriteAllText(Path.Combine(root, ".complete"), "complete\n");
            return root;
        }

        public void Dispose()
        {
            try { if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
