using System.Collections;
using Karpik.Engine.Sdk.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

public sealed class RuntimeBundleTaskTests
{
    [Theory]
    [InlineData("Client")]
    [InlineData("Server")]
    public void Execute_PublishesVersionedSidePureBundle(string side)
    {
        using var tree = new TemporaryTree();
        string primary = tree.Write($"output/Game.{side}.dll", side);
        string shared = tree.Write("output/Game.Shared.dll", "shared");
        string content = tree.Write("assets/settings.json", "{}");
        string bundle = Path.Combine(tree.Root, "publish", "karpik-bundle");
        var engine = new FakeBuildEngine();
        var task = new BuildKarpikRuntimeBundleTask
        {
            BuildEngine = engine,
            Side = side,
            PrimaryAssembly = primary,
            BundlePath = bundle,
            Assemblies = [new TaskItem(shared), new TaskItem(primary)],
            Content = [ContentItem(content, "Config/settings.json")]
        };

        Assert.True(task.Execute());
        Assert.Empty(engine.Errors);
        Assert.Equal($"karpik-runtime-side-v1:{side}\n", File.ReadAllText(Path.Combine(bundle, "runtime-bundle.side")));
        Assert.Equal("karpik-runtime-bundle-v1\n", File.ReadAllText(Path.Combine(bundle, ".complete")));
        Assert.Equal("karpik-module-staging-v1\n", File.ReadAllText(Path.Combine(bundle, "modules.version.1", ".complete")));
        string[] expectedModules = [.. new[] { $"Game.{side}.dll", "Game.Shared.dll" }.Order(StringComparer.Ordinal)];
        string moduleList = Path.Combine(bundle, "modules.version.1", "modules.list");
        Assert.Equal(expectedModules, File.ReadAllLines(moduleList));
        Assert.Equal(string.Join('\n', expectedModules) + '\n', File.ReadAllText(moduleList));
        Assert.True(File.Exists(Path.Combine(bundle, "modules.version.1", $"Game.{side}.dll")));
        Assert.True(File.Exists(Path.Combine(bundle, "modules.version.1", "Game.Shared.dll")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(bundle, "Content", "Config", "settings.json")));
        Assert.False(File.Exists(Path.Combine(bundle, ".karpik-owned-staging")));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories),
            path => Path.GetFileName(path).StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_RejectsRelativeDestinationAndMissingRequiredInputs()
    {
        var engine = new FakeBuildEngine();
        var task = new BuildKarpikRuntimeBundleTask
        {
            BuildEngine = engine,
            Side = "Client",
            PrimaryAssembly = "missing.dll",
            BundlePath = "relative"
        };

        Assert.False(task.Execute());
        Assert.NotEmpty(engine.Errors);
    }

    [Fact]
    public void Execute_RejectsContentTraversalAndRunnerAssembly()
    {
        using var tree = new TemporaryTree();
        string primary = tree.Write("output/Game.Client.dll", "game");
        string runner = tree.Write("output/Karpik.Engine.Core.Runner.dll", "runner");
        string content = tree.Write("assets/content.txt", "content");
        var traversalEngine = new FakeBuildEngine();
        var traversalTask = new BuildKarpikRuntimeBundleTask
        {
            BuildEngine = traversalEngine,
            Side = "Client",
            PrimaryAssembly = primary,
            BundlePath = Path.Combine(tree.Root, "traversal-bundle"),
            Assemblies = [new TaskItem(primary)],
            Content = [ContentItem(content, "../escaped.txt")]
        };

        Assert.False(traversalTask.Execute());
        Assert.False(File.Exists(Path.Combine(tree.Root, "escaped.txt")));

        var runnerEngine = new FakeBuildEngine();
        var runnerTask = new BuildKarpikRuntimeBundleTask
        {
            BuildEngine = runnerEngine,
            Side = "Client",
            PrimaryAssembly = primary,
            BundlePath = Path.Combine(tree.Root, "runner-bundle"),
            Assemblies = [new TaskItem(primary), new TaskItem(runner)],
            Content = [ContentItem(content, "content.txt")]
        };

        Assert.False(runnerTask.Execute());
        Assert.False(Directory.Exists(Path.Combine(tree.Root, "runner-bundle")));
    }

    [Fact]
    public void Execute_RejectsLinkedAssemblyWhenSymbolicLinksAreAvailable()
    {
        using var tree = new TemporaryTree();
        string realAssembly = tree.Write("output/Game.Client.dll", "game");
        string linkedAssembly = Path.Combine(tree.Root, "linked.dll");
        try
        {
            File.CreateSymbolicLink(linkedAssembly, realAssembly);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }
        var engine = new FakeBuildEngine();
        var task = new BuildKarpikRuntimeBundleTask
        {
            BuildEngine = engine,
            Side = "Client",
            PrimaryAssembly = linkedAssembly,
            BundlePath = Path.Combine(tree.Root, "bundle"),
            Assemblies = [new TaskItem(linkedAssembly)],
            Content = [ContentItem(tree.Write("content.txt", "content"), "content.txt")]
        };

        Assert.False(task.Execute());
        Assert.NotEmpty(engine.Errors);
    }

    [Fact]
    public void Execute_RefusesToReplaceUnprovenUserDirectory()
    {
        using var tree = new TemporaryTree();
        string destination = Path.Combine(tree.Root, "karpik-bundle");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "user.txt"), "preserve");
        string primary = tree.Write("output/Game.Client.dll", "game");
        var task = new BuildKarpikRuntimeBundleTask
        {
            BuildEngine = new FakeBuildEngine(),
            Side = "Client",
            PrimaryAssembly = primary,
            BundlePath = destination,
            Assemblies = [new TaskItem(primary)],
            Content = [ContentItem(tree.Write("content.txt", "content"), "content.txt")]
        };

        Assert.False(task.Execute());
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(destination, "user.txt")));
    }

    [Fact]
    public void Execute_FailurePreservesLastCompleteBundle()
    {
        using var tree = new TemporaryTree();
        string primary = tree.Write("output/Game.Client.dll", "new");
        string bundle = Path.Combine(tree.Root, "karpik-bundle");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "runtime-bundle.side"), "karpik-runtime-side-v1:Client\n");
        File.WriteAllText(Path.Combine(bundle, ".complete"), "karpik-runtime-bundle-v1\n");
        Directory.CreateDirectory(Path.Combine(bundle, "modules.version.1"));
        File.WriteAllText(Path.Combine(bundle, "modules.version.1", ".complete"), "karpik-module-staging-v1\n");
        File.WriteAllText(Path.Combine(bundle, "modules.version.1", "modules.list"), "Game.Client.dll\n");
        File.WriteAllText(Path.Combine(bundle, "modules.version.1", "Game.Client.dll"), "old-game");
        Directory.CreateDirectory(Path.Combine(bundle, "Content"));
        File.WriteAllText(Path.Combine(bundle, "Content", "content.txt"), "old-content");
        File.WriteAllText(Path.Combine(bundle, "sentinel.txt"), "old");
        var engine = new FakeBuildEngine();
        var task = new BuildKarpikRuntimeBundleTask(new FailingPublishFileSystem())
        {
            BuildEngine = engine,
            Side = "Client",
            PrimaryAssembly = primary,
            BundlePath = bundle,
            Assemblies = [new TaskItem(primary)],
            Content = [ContentItem(tree.Write("assets/content.txt", "content"), "content.txt")]
        };

        Assert.False(task.Execute());
        Assert.Equal("old", File.ReadAllText(Path.Combine(bundle, "sentinel.txt")));
        Assert.Equal("karpik-runtime-bundle-v1\n", File.ReadAllText(Path.Combine(bundle, ".complete")));
    }

    [Fact]
    public void Execute_MarkerCleanupFailureRestoresLastCompleteBundle()
    {
        using var tree = new TemporaryTree();
        string primary = tree.Write("output/Game.Client.dll", "new");
        string bundle = CreateCompleteBundle(tree, "old");
        var task = new BuildKarpikRuntimeBundleTask(new FailingMarkerCleanupFileSystem())
        {
            BuildEngine = new FakeBuildEngine(),
            Side = "Client",
            PrimaryAssembly = primary,
            BundlePath = bundle,
            Assemblies = [new TaskItem(primary)],
            Content = [ContentItem(tree.Write("assets/content.txt", "new-content"), "content.txt")]
        };

        Assert.False(task.Execute());
        Assert.Equal("old", File.ReadAllText(Path.Combine(bundle, "sentinel.txt")));
        Assert.False(File.Exists(Path.Combine(bundle, ".karpik-owned-staging")));
    }

    [Fact]
    public void Execute_RepeatedPublicationIsDeterministicAndLeavesNoInterruptedState()
    {
        using var tree = new TemporaryTree();
        string primary = tree.Write("output/Game.Client.dll", "game");
        string shared = tree.Write("output/Game.Shared.dll", "shared");
        string content = tree.Write("assets/content.txt", "content");
        string bundle = Path.Combine(tree.Root, "publish", "karpik-bundle");
        var task = new BuildKarpikRuntimeBundleTask
        {
            BuildEngine = new FakeBuildEngine(),
            Side = "Client",
            PrimaryAssembly = primary,
            BundlePath = bundle,
            Assemblies = [new TaskItem(shared), new TaskItem(primary)],
            Content = [ContentItem(content, "content.txt")]
        };

        Assert.True(task.Execute());
        string[] first = Snapshot(bundle);
        Assert.True(task.Execute());

        Assert.Equal(first, Snapshot(bundle));
        Assert.False(Directory.Exists(bundle + ".previous"));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(bundle)!, "karpik-bundle.staging.*"));

        static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => $"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{File.ReadAllText(path)}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string CreateCompleteBundle(TemporaryTree tree, string sentinel)
    {
        string bundle = Path.Combine(tree.Root, "complete-bundle");
        Directory.CreateDirectory(Path.Combine(bundle, "modules.version.1"));
        Directory.CreateDirectory(Path.Combine(bundle, "Content"));
        File.WriteAllText(Path.Combine(bundle, "runtime-bundle.side"), "karpik-runtime-side-v1:Client\n");
        File.WriteAllText(Path.Combine(bundle, ".complete"), "karpik-runtime-bundle-v1\n");
        File.WriteAllText(Path.Combine(bundle, "modules.version.1", ".complete"), "karpik-module-staging-v1\n");
        File.WriteAllText(Path.Combine(bundle, "modules.version.1", "modules.list"), "Game.Client.dll\n");
        File.WriteAllText(Path.Combine(bundle, "modules.version.1", "Game.Client.dll"), "old-game");
        File.WriteAllText(Path.Combine(bundle, "Content", "content.txt"), "old-content");
        File.WriteAllText(Path.Combine(bundle, "sentinel.txt"), sentinel);
        return bundle;
    }

    private static TaskItem ContentItem(string path, string targetPath)
    {
        var item = new TaskItem(path);
        item.SetMetadata("TargetPath", targetPath);
        return item;
    }

    private sealed class FailingPublishFileSystem : RuntimeBundleFileSystem
    {
        private int _moves;

        public override void MoveDirectory(string source, string destination)
        {
            _moves++;
            if (_moves == 2)
            {
                throw new IOException("Injected publish failure.");
            }
            base.MoveDirectory(source, destination);
        }
    }

    private sealed class FailingMarkerCleanupFileSystem : RuntimeBundleFileSystem
    {
        public override void DeleteFile(string path) => throw new IOException("Injected marker cleanup failure.");
    }

    private sealed class TemporaryTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "KarpikBundleTaskTests", Guid.NewGuid().ToString("N"));

        public TemporaryTree() => Directory.CreateDirectory(Root);

        public string Write(string relativePath, string contents)
        {
            string path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class FakeBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;
    }
}
