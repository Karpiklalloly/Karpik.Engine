using System.Collections;
using System.Text;
using Karpik.Engine.Sdk.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

public sealed class RuntimeBundleTaskTests
{
    [Fact]
    public void SdkAndRuntimeBundleContractsKeepMarkersAndBoundsSynchronized()
    {
        Assert.Equal(Karpik.Engine.Core.RuntimeBundleLayout.MaxTreeEntries, BuildKarpikRuntimeBundleTask.MaxTreeEntries);
        Assert.Equal(Karpik.Engine.Core.RuntimeBundleLayout.MaxTreeDepth, BuildKarpikRuntimeBundleTask.MaxTreeDepth);
        Assert.Equal(Karpik.Engine.Core.RuntimeBundleLayout.MaxManifestBytes, BuildKarpikRuntimeBundleTask.MaxManifestBytes);
        Assert.Equal(Karpik.Engine.Core.RuntimeBundleLayout.MaxManifestEntries, BuildKarpikRuntimeBundleTask.MaxManifestEntries);
        Assert.Equal(Karpik.Engine.Core.RuntimeBundleLayout.MaxIndividualFileBytes, BuildKarpikRuntimeBundleTask.MaxIndividualFileBytes);
        Assert.Equal(Karpik.Engine.Core.RuntimeBundleLayout.MaxBundleBytes, BuildKarpikRuntimeBundleTask.MaxBundleBytes);
        Assert.Equal(Karpik.Engine.Core.RuntimeBundleLayout.BundleCompletionMarker, BuildKarpikRuntimeBundleTask.BundleCompletionMarker);
        Assert.Equal(Karpik.Engine.Core.RuntimeBundleLayout.ModuleCompletionMarker, BuildKarpikRuntimeBundleTask.ModuleCompletionMarker);
        Assert.Equal(Karpik.Engine.Core.RuntimeBundleLayout.SideMarkerPrefix, BuildKarpikRuntimeBundleTask.SideMarkerPrefix);
    }

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
    public void Execute_RejectsOversizeAssemblyBeforeCopyingAndCountsContentRootInDepth()
    {
        using var tree = new TemporaryTree();
        string oversized = tree.Write("output/Oversized.Client.dll", string.Empty);
        using (FileStream stream = File.OpenWrite(oversized))
        {
            stream.SetLength(BuildKarpikRuntimeBundleTask.MaxIndividualFileBytes + 1);
        }
        string content = tree.Write("assets/content.txt", "content");
        string oversizeBundle = Path.Combine(tree.Root, "oversize-bundle");
        var oversizeTask = new BuildKarpikRuntimeBundleTask
        {
            BuildEngine = new FakeBuildEngine(),
            Side = "Client",
            PrimaryAssembly = oversized,
            BundlePath = oversizeBundle,
            Assemblies = [new TaskItem(oversized)],
            Content = [ContentItem(content, "content.txt")]
        };

        Assert.False(oversizeTask.Execute());
        Assert.False(Directory.Exists(oversizeBundle));
        Assert.Empty(Directory.EnumerateDirectories(tree.Root, "oversize-bundle.staging.*"));

        string primary = tree.Write("output/Game.Client.dll", "game");
        string[] segments = Enumerable.Range(0, BuildKarpikRuntimeBundleTask.MaxTreeDepth - 1)
            .Select(index => $"d{index}")
            .Append("content.txt")
            .ToArray();
        var deepTask = new BuildKarpikRuntimeBundleTask
        {
            BuildEngine = new FakeBuildEngine(),
            Side = "Client",
            PrimaryAssembly = primary,
            BundlePath = Path.Combine(tree.Root, "deep-bundle"),
            Assemblies = [new TaskItem(primary)],
            Content = [ContentItem(content, Path.Combine(segments))]
        };

        Assert.False(deepTask.Execute());
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
        File.WriteAllText(Path.Combine(bundle, "Content", "sentinel.txt"), "old");
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
        Assert.Equal("old", File.ReadAllText(Path.Combine(bundle, "Content", "sentinel.txt")));
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
        Assert.Equal("old", File.ReadAllText(Path.Combine(bundle, "Content", "sentinel.txt")));
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

    [Theory]
    [InlineData("crlf-manifest")]
    [InlineData("bom-manifest")]
    [InlineData("unsafe-manifest")]
    [InlineData("missing-listed-dll")]
    [InlineData("unlisted-dll")]
    [InlineData("extra-module-directory")]
    [InlineData("unexpected-root-file")]
    public void Execute_RefusesMalformedBackupWithoutMovingOrDeletingIt(string mutation)
    {
        using var tree = new TemporaryTree();
        string destination = Path.Combine(tree.Root, "publish", "karpik-bundle");
        string backup = destination + ".previous";
        string complete = CreateCompleteBundle(tree, "preserve");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(complete, backup);
        string modules = Path.Combine(backup, "modules.version.1");
        string manifest = Path.Combine(modules, "modules.list");
        switch (mutation)
        {
            case "crlf-manifest":
                File.WriteAllText(manifest, "Game.Client.dll\r\n", new UTF8Encoding(false));
                break;
            case "bom-manifest":
                File.WriteAllText(manifest, "Game.Client.dll\n", new UTF8Encoding(true));
                break;
            case "unsafe-manifest":
                File.WriteAllText(manifest, "../Game.Client.dll\n", new UTF8Encoding(false));
                break;
            case "missing-listed-dll":
                File.Delete(Path.Combine(modules, "Game.Client.dll"));
                break;
            case "unlisted-dll":
                File.WriteAllText(Path.Combine(modules, "Unlisted.dll"), "unlisted");
                break;
            case "extra-module-directory":
                Directory.CreateDirectory(Path.Combine(backup, "modules.version.2"));
                break;
            case "unexpected-root-file":
                File.WriteAllText(Path.Combine(backup, "user-owned.txt"), "do-not-delete");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var task = new BuildKarpikRuntimeBundleTask
        {
            BuildEngine = new FakeBuildEngine(),
            Side = "Client",
            PrimaryAssembly = tree.Write("output/Game.Client.dll", "new"),
            BundlePath = destination,
            Content = [ContentItem(tree.Write("assets/content.txt", "new-content"), "content.txt")]
        };

        Assert.False(task.Execute());
        Assert.True(Directory.Exists(backup));
        Assert.False(Directory.Exists(destination));
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(backup, "Content", "sentinel.txt")));
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
        File.WriteAllText(Path.Combine(bundle, "Content", "sentinel.txt"), sentinel);
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
