using Karpik.Editor;
using Karpik.Engine.Core;
using Karpik.Engine.ProjectModel;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class ProjectRuntimeResolverTests
{
    [Fact]
    public void Resolve_PairsInstalledEngineRunnersWithActiveGameBundles()
    {
        RuntimeLayout layout = CreateBundleStructure();
        try
        {
            var resolver = new ProjectRuntimeResolver(CreateRuntimeDescriptor(layout));

            ProjectRuntimeResolver.EditorRuntimeDescriptor client = resolver.Resolve(Side.Client);
            ProjectRuntimeResolver.EditorRuntimeDescriptor server = resolver.Resolve(Side.Server);

            Assert.Equal(Side.Client, client.Side);
            Assert.Equal(layout.EngineRoot, client.EngineRoot);
            Assert.Equal(layout.ClientBundle, client.BundlePath);
            Assert.Equal(layout.ClientRunner, client.RunnerPath);
            Assert.Equal(Side.Server, server.Side);
            Assert.Equal(layout.EngineRoot, server.EngineRoot);
            Assert.Equal(layout.ServerBundle, server.BundlePath);
            Assert.Equal(layout.ServerRunner, server.RunnerPath);
        }
        finally
        {
            Directory.Delete(layout.Root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsBundleForAnotherSide()
    {
        RuntimeLayout layout = CreateBundleStructure();
        try
        {
            string manifestPath = Path.Combine(layout.ServerBundle, "runtime-bundle.side");
            WriteSideMarker(manifestPath, Side.Client);
            var factory = new EditorPreviewBackendFactory(new ProjectRuntimeResolver(CreateRuntimeDescriptor(layout)));

            Assert.Throws<InvalidDataException>(() => factory.Create(Side.Server));
        }
        finally
        {
            Directory.Delete(layout.Root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsBundleWithoutWorker()
    {
        RuntimeLayout layout = CreateBundleStructure();
        try
        {
            File.Delete(layout.ClientRunner);
            var factory = new EditorPreviewBackendFactory(new ProjectRuntimeResolver(CreateRuntimeDescriptor(layout)));

            Assert.Throws<FileNotFoundException>(() => factory.Create(Side.Client));
        }
        finally
        {
            Directory.Delete(layout.Root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsBundleWithoutCompletedModuleStaging()
    {
        RuntimeLayout layout = CreateBundleStructure();
        try
        {
            string bundleDirectory = layout.ServerBundle;
            string moduleDirectory = Assert.Single(
                Directory.GetDirectories(bundleDirectory, "modules.version.*"));
            File.Delete(Path.Combine(moduleDirectory, ".complete"));
            var factory = new EditorPreviewBackendFactory(new ProjectRuntimeResolver(CreateRuntimeDescriptor(layout)));

            Assert.Throws<InvalidDataException>(() => factory.Create(Side.Server));
        }
        finally
        {
            Directory.Delete(layout.Root, recursive: true);
        }
    }

    [Fact]
    public void EditorPreviewBackendFactory_CreatesBackendForRequestedBundleSide()
    {
        RuntimeLayout layout = CreateBundleStructure();
        try
        {
            var factory = new EditorPreviewBackendFactory(new ProjectRuntimeResolver(CreateRuntimeDescriptor(layout)));

            using IEditorBackend backend = factory.Create(Side.Client);

            Assert.Equal(Side.Client, backend.Side);
            Assert.Equal(EditorPreviewState.Stopped, backend.State);
            Assert.Null(backend.ProcessId);
        }
        finally
        {
            Directory.Delete(layout.Root, recursive: true);
        }
    }

    private static ProjectRuntimeDescriptor CreateRuntimeDescriptor(RuntimeLayout layout)
    {
        return new ProjectRuntimeDescriptor(
            layout.EngineRoot,
            layout.ClientBundle,
            layout.ServerBundle,
            layout.ClientRunner,
            layout.ServerRunner);
    }

    private static RuntimeLayout CreateBundleStructure()
    {
        string root = Path.Combine(Path.GetTempPath(), $"KarpikEditorBundleTests-{Guid.NewGuid():N}");
        string engineRoot = Path.Combine(root, "engine");
        string gameRoot = Path.Combine(root, "game");

        foreach (Side side in new[] { Side.Client, Side.Server })
        {
            string sideName = side.ToString().ToLowerInvariant();
            string projectName = side == Side.Client ? "ActiveGame.Client" : "ActiveGame.Server";
            string bundleDirectory = Path.Combine(
                gameRoot, "Source", projectName, "bin", "Debug", "net10.0", "karpik-bundle");
            Directory.CreateDirectory(bundleDirectory);

            string runnerName = OperatingSystem.IsWindows()
                ? "Karpik.Engine.Core.Runner.exe"
                : "Karpik.Engine.Core.Runner";
            string runnerDirectory = Path.Combine(engineRoot, "runners", sideName);
            Directory.CreateDirectory(runnerDirectory);
            File.WriteAllText(Path.Combine(runnerDirectory, runnerName), string.Empty);

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

        return new RuntimeLayout(root, engineRoot, gameRoot);
    }

    private static void WriteSideMarker(string path, Side side)
    {
        File.WriteAllText(path, $"karpik-runtime-side-v1:{side}\n");
    }

    private sealed class RuntimeLayout(string root, string engineRoot, string gameRoot)
    {
        private static string RunnerName => OperatingSystem.IsWindows()
            ? "Karpik.Engine.Core.Runner.exe"
            : "Karpik.Engine.Core.Runner";

        public string Root { get; } = root;
        public string EngineRoot { get; } = engineRoot;
        public string GameRoot { get; } = gameRoot;
        public string ClientBundle => GetBundlePath(Side.Client);
        public string ServerBundle => GetBundlePath(Side.Server);
        public string ClientRunner => GetRunnerPath(Side.Client);
        public string ServerRunner => GetRunnerPath(Side.Server);

        private string GetBundlePath(Side side)
        {
            string projectName = side == Side.Client ? "ActiveGame.Client" : "ActiveGame.Server";
            return Path.Combine(GameRoot, "Source", projectName, "bin", "Debug", "net10.0", "karpik-bundle");
        }

        private string GetRunnerPath(Side side) => Path.Combine(
            EngineRoot,
            "runners",
            side.ToString().ToLowerInvariant(),
            RunnerName);
    }
}
