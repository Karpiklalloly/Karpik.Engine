using System.Text.Json;
using System.Text.Json.Serialization;
using Karpik.Editor;
using Karpik.Engine.Core;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class RuntimeBundleResolverTests
{
    [Fact]
    public void Resolve_ReturnsCompletedBundleForRequestedSide()
    {
        string root = CreateBundle(Side.Server);
        try
        {
            var resolver = new RuntimeBundleResolver(root);

            EditorRuntimeBundle bundle = resolver.Resolve(Side.Server);

            Assert.Equal(Side.Server, bundle.Side);
            Assert.Equal(Path.Combine(root, "runtimes", "server"), bundle.DirectoryPath);
            Assert.True(File.Exists(bundle.WorkerExecutablePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsBundleForAnotherSide()
    {
        string root = CreateBundle(Side.Server);
        try
        {
            string manifestPath = Path.Combine(root, "runtimes", "server", "runtime-bundle.json");
            WriteManifest(manifestPath, Side.Client);
            var resolver = new RuntimeBundleResolver(root);

            Assert.Throws<InvalidDataException>(() => resolver.Resolve(Side.Server));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsBundleWithoutWorker()
    {
        string root = CreateBundle(Side.Client);
        try
        {
            string workerName = OperatingSystem.IsWindows()
                ? "Karpik.Engine.Core.Runner.exe"
                : "Karpik.Engine.Core.Runner";
            File.Delete(Path.Combine(root, "runtimes", "client", workerName));
            var resolver = new RuntimeBundleResolver(root);

            Assert.Throws<FileNotFoundException>(() => resolver.Resolve(Side.Client));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsBundleWithoutCompletedModuleStaging()
    {
        string root = CreateBundle(Side.Server);
        try
        {
            string bundleDirectory = Path.Combine(root, "runtimes", "server");
            string moduleDirectory = Assert.Single(
                Directory.GetDirectories(bundleDirectory, "modules.version.*"));
            File.Delete(Path.Combine(moduleDirectory, ".complete"));
            var resolver = new RuntimeBundleResolver(root);

            Assert.Throws<InvalidDataException>(() => resolver.Resolve(Side.Server));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EditorPreviewBackendFactory_CreatesBackendForRequestedBundleSide()
    {
        string root = CreateBundle(Side.Client);
        try
        {
            var factory = new EditorPreviewBackendFactory(new RuntimeBundleResolver(root));

            using IEditorBackend backend = factory.Create(Side.Client);

            Assert.Equal(Side.Client, backend.Side);
            Assert.Equal(EditorPreviewState.Stopped, backend.State);
            Assert.Null(backend.ProcessId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateBundle(Side side)
    {
        string root = Path.Combine(Path.GetTempPath(), $"KarpikEditorBundleTests-{Guid.NewGuid():N}");
        string sideName = side.ToString().ToLowerInvariant();
        string bundleDirectory = Path.Combine(root, "runtimes", sideName);
        Directory.CreateDirectory(bundleDirectory);

        string workerName = OperatingSystem.IsWindows()
            ? "Karpik.Engine.Core.Runner.exe"
            : "Karpik.Engine.Core.Runner";
        File.WriteAllText(Path.Combine(bundleDirectory, workerName), string.Empty);

        string moduleDirectory = Path.Combine(bundleDirectory, $"modules.version.{Guid.NewGuid():N}");
        Directory.CreateDirectory(moduleDirectory);
        File.WriteAllText(Path.Combine(moduleDirectory, ".complete"), string.Empty);

        WriteManifest(Path.Combine(bundleDirectory, "runtime-bundle.json"), side);

        return root;
    }

    private static void WriteManifest(string path, Side side)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(path, JsonSerializer.Serialize(new RuntimeBundleManifest(side), options));
    }
}
