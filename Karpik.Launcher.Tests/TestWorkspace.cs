using Karpik.Engine.Tooling;

namespace Karpik.Launcher.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "karpik-launcher-tests", Guid.NewGuid().ToString("N"));
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

    public string CreateInstallation(string localRoot, string name, string engineVersion, string sdkVersion)
    {
        string root = Path.Combine(localRoot, "Karpik", "Engines", name);
        Directory.CreateDirectory(Path.Combine(root, "editor"));
        Directory.CreateDirectory(Path.Combine(root, "sdk"));
        Directory.CreateDirectory(Path.Combine(root, "runners", "client"));
        Directory.CreateDirectory(Path.Combine(root, "runners", "server"));
        Directory.CreateDirectory(Path.Combine(root, "modules", "Module"));
        Directory.CreateDirectory(Path.Combine(root, "native"));
        File.WriteAllText(Path.Combine(root, "editor", "Karpik.Editor.dll"), engineVersion);
        File.WriteAllText(Path.Combine(root, "sdk", $"Karpik.Engine.Sdk.{sdkVersion}.nupkg"), "sdk");
        File.WriteAllText(Path.Combine(root, "runners", "client", "Karpik.Engine.Core.Runner.dll"), "client");
        File.WriteAllText(Path.Combine(root, "runners", "server", "Karpik.Engine.Core.Runner.dll"), "server");
        File.WriteAllText(Path.Combine(root, "modules", "Module", "Module.dll"), "module");
        File.WriteAllText(
            Path.Combine(root, "modules", EngineModuleCatalog.FileName),
            EngineModuleCatalog.Serialize([
                new EngineModuleCatalogEntry("Module", EngineModuleSide.Shared)
            ]));
        var manifest = new EngineInstallationManifest
        {
            EngineVersion = engineVersion,
            MsBuildSdkVersion = sdkVersion,
            EditorVersion = engineVersion,
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
        try
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
