using Karpik.Engine.Tooling;

namespace Karpik.Engine.Tooling.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "karpik-tooling-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class TestInstallation
{
    public static string Create(
        string parent,
        string directoryName = "0.6.0",
        string engineVersion = "0.6.0",
        string sdkVersion = "0.6.0-sdk",
        int layoutVersion = EngineInstallationManifest.CurrentLayoutVersion,
        int runtimeProtocolVersion = EngineInstallationManifest.CurrentRuntimeProtocolVersion)
    {
        string root = Path.Combine(parent, directoryName);
        Directory.CreateDirectory(Path.Combine(root, "editor"));
        Directory.CreateDirectory(Path.Combine(root, "sdk"));
        Directory.CreateDirectory(Path.Combine(root, "runners", "client"));
        Directory.CreateDirectory(Path.Combine(root, "runners", "server"));
        Directory.CreateDirectory(Path.Combine(root, "modules", "Module"));
        Directory.CreateDirectory(Path.Combine(root, "native"));
        Directory.CreateDirectory(Path.Combine(root, "shared"));
        File.WriteAllText(Path.Combine(root, "editor", "Karpik.Editor.dll"), "editor");
        File.WriteAllText(Path.Combine(root, "sdk", $"Karpik.Engine.Sdk.{sdkVersion}.nupkg"), "sdk");
        File.WriteAllText(Path.Combine(root, "runners", "client", "Karpik.Engine.Core.Runner.dll"), "client");
        File.WriteAllText(Path.Combine(root, "runners", "server", "Karpik.Engine.Core.Runner.dll"), "server");
        File.WriteAllText(Path.Combine(root, "modules", "Module", "Module.dll"), "module");
        File.WriteAllText(
            Path.Combine(root, "modules", EngineModuleCatalog.FileName),
            EngineModuleCatalog.Serialize([
                new EngineModuleCatalogEntry("Module", EngineModuleSide.Shared)
            ]));

        WriteManifest(
            root,
            engineVersion,
            sdkVersion,
            layoutVersion,
            runtimeProtocolVersion,
            EngineContentHash.Compute(root));
        File.WriteAllText(Path.Combine(root, ".complete"), "complete\n");
        return root;
    }

    public static void RewriteManifest(
        string root,
        string? engineVersion = null,
        string? sdkVersion = null,
        int? layoutVersion = null,
        int? runtimeProtocolVersion = null,
        string? contentHash = null,
        string? editorVersion = null)
    {
        EngineInstallationManifest current = EngineInstallationManifest.Parse(
            File.ReadAllText(Path.Combine(root, "engine-installation.json")));
        WriteManifest(
            root,
            engineVersion ?? current.EngineVersion,
            sdkVersion ?? current.MsBuildSdkVersion,
            layoutVersion ?? current.LayoutVersion,
            runtimeProtocolVersion ?? current.RuntimeProtocolVersion,
            contentHash ?? current.ContentHash,
            editorVersion ?? current.EditorVersion);
    }

    private static void WriteManifest(
        string root,
        string engineVersion,
        string sdkVersion,
        int layoutVersion,
        int runtimeProtocolVersion,
        string contentHash,
        string? editorVersion = null)
    {
        var manifest = new EngineInstallationManifest
        {
            EngineVersion = engineVersion,
            MsBuildSdkVersion = sdkVersion,
            EditorVersion = editorVersion ?? engineVersion,
            LayoutVersion = layoutVersion,
            RuntimeProtocolVersion = runtimeProtocolVersion,
            ContentHash = contentHash
        };
        File.WriteAllText(Path.Combine(root, "engine-installation.json"), manifest.ToJson());
    }
}
