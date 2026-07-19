using System.Text.Json;
using System.Text.Json.Serialization;
using Karpik.Editor;
using Karpik.Engine.Core;
using Karpik.Engine.ProjectModel;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class ProjectRuntimeResolverTests
{
    [Fact]
    public void Resolve_ReturnsCompletedBundleForRequestedSide()
    {
        string root = CreateBundleStructure();
        try
        {
            var resolver = new ProjectRuntimeResolver(CreateRuntimeDescriptor(root));

            ProjectRuntimeResolver.EditorRuntimeDescriptor bundle = resolver.Resolve(Side.Server);

            Assert.Equal(Side.Server, bundle.Side);
            Assert.Equal(Path.Combine(root, "runtimes", "server", "karpik-bundle"), bundle.BundlePath);
            Assert.True(File.Exists(bundle.RunnerPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsBundleForAnotherSide()
    {
        string root = CreateBundleStructure();
        try
        {
            string manifestPath = Path.Combine(root, "runtimes", "server", "karpik-bundle", "runtime-bundle.side");
            WriteSideMarker(manifestPath, Side.Client);
            var factory = new EditorPreviewBackendFactory(new ProjectRuntimeResolver(CreateRuntimeDescriptor(root)));

            Assert.Throws<InvalidDataException>(() => factory.Create(Side.Server));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsBundleWithoutWorker()
    {
        string root = CreateBundleStructure();
        try
        {
            string workerName = OperatingSystem.IsWindows()
                ? "Karpik.Engine.Core.Runner.exe"
                : "Karpik.Engine.Core.Runner";
            File.Delete(Path.Combine(root, "runtimes", "client", workerName));
            var factory = new EditorPreviewBackendFactory(new ProjectRuntimeResolver(CreateRuntimeDescriptor(root)));

            Assert.Throws<FileNotFoundException>(() => factory.Create(Side.Client));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsBundleWithoutCompletedModuleStaging()
    {
        string root = CreateBundleStructure();
        try
        {
            string bundleDirectory = Path.Combine(root, "runtimes", "server", "karpik-bundle");
            string moduleDirectory = Assert.Single(
                Directory.GetDirectories(bundleDirectory, "modules.version.*"));
            File.Delete(Path.Combine(moduleDirectory, ".complete"));
            var factory = new EditorPreviewBackendFactory(new ProjectRuntimeResolver(CreateRuntimeDescriptor(root)));

            Assert.Throws<InvalidDataException>(() => factory.Create(Side.Server));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EditorPreviewBackendFactory_CreatesBackendForRequestedBundleSide()
    {
        string root = CreateBundleStructure();
        try
        {
            var factory = new EditorPreviewBackendFactory(new ProjectRuntimeResolver(CreateRuntimeDescriptor(root)));

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

    private static ProjectRuntimeDescriptor CreateRuntimeDescriptor(string root)
    {
        string runnerName = OperatingSystem.IsWindows()
            ? "Karpik.Engine.Core.Runner.exe"
            : "Karpik.Engine.Core.Runner";
        return new ProjectRuntimeDescriptor(
            root,
            Path.Combine(root, "runtimes", "client", "karpik-bundle"),
            Path.Combine(root, "runtimes", "server", "karpik-bundle"),
            Path.Combine(root, "runtimes", "client", runnerName),
            Path.Combine(root, "runtimes", "server", runnerName));
    }

    private static string CreateBundleStructure()
    {
        string root = Path.Combine(Path.GetTempPath(), $"KarpikEditorBundleTests-{Guid.NewGuid():N}");

        foreach (Side side in new[] { Side.Client, Side.Server })
        {
            string sideName = side.ToString().ToLowerInvariant();
            string bundleDirectory = Path.Combine(root, "runtimes", sideName, "karpik-bundle");
            Directory.CreateDirectory(bundleDirectory);

            string runnerName = OperatingSystem.IsWindows()
                ? "Karpik.Engine.Core.Runner.exe"
                : "Karpik.Engine.Core.Runner";
            File.WriteAllText(Path.Combine(root, "runtimes", sideName, runnerName), string.Empty);

            // Create .complete marker for bundle
            File.WriteAllText(Path.Combine(bundleDirectory, ".complete"), "karpik-runtime-bundle-v1\n");

            // Create side marker
            File.WriteAllText(Path.Combine(bundleDirectory, "runtime-bundle.side"), $"karpik-runtime-side-v1:{side}\n");

            // Create Content directory with at least one file
            string contentDir = Path.Combine(bundleDirectory, "Content");
            Directory.CreateDirectory(contentDir);
            File.WriteAllText(Path.Combine(contentDir, "placeholder.txt"), "placeholder");

            // Create module staging directory
            string moduleDirectory = Path.Combine(bundleDirectory, "modules.version.1");
            Directory.CreateDirectory(moduleDirectory);
            File.WriteAllText(Path.Combine(moduleDirectory, ".complete"), "karpik-module-staging-v1\n");

            // Create TestModule.dll file and modules.list manifest
            File.WriteAllText(Path.Combine(moduleDirectory, "TestModule.dll"), "fake dll content");
            File.WriteAllText(Path.Combine(moduleDirectory, "modules.list"), "TestModule.dll\n");
        }

        return root;
    }

    private static void WriteSideMarker(string path, Side side)
    {
        File.WriteAllText(path, $"karpik-runtime-side-v1:{side}\n");
    }
}
