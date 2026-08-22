using Karpik.Engine.Core;
using Xunit;

public sealed class RuntimeLaunchOptionsTests
{
    [Theory]
    [InlineData(Side.Client)]
    [InlineData(Side.Server)]
    public void Constructor_AcceptsMatchingCompleteBundle(Side side)
    {
        using var tree = new RuntimeTree(side);

        var options = new RuntimeLaunchOptions(side, tree.RunnerPath, tree.BundlePath, tree.EngineRoot);

        Assert.Equal(side, options.Side);
        Assert.Equal(tree.RunnerPath, options.RunnerExecutablePath);
        Assert.Equal(tree.BundlePath, options.BundlePath);
        Assert.Equal(tree.EngineRoot, options.EngineRoot);
    }

    [Fact]
    public void Constructor_RejectsWrongSideIncompleteAndRelativeBundles()
    {
        using var tree = new RuntimeTree(Side.Server);

        Assert.Throws<InvalidDataException>(() => new RuntimeLaunchOptions(Side.Client, tree.RunnerPath, tree.BundlePath, tree.EngineRoot));
        File.Delete(Path.Combine(tree.BundlePath, ".complete"));
        Assert.Throws<InvalidDataException>(() => new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, tree.BundlePath, tree.EngineRoot));
        Assert.Throws<ArgumentException>(() => new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, "relative", tree.EngineRoot));
    }

    [Fact]
    public void ProcessStartInfo_UsesEngineRunnerBundleWorkingDirectoryAndArgumentList()
    {
        using var tree = new RuntimeTree(Side.Client);
        var launch = new RuntimeLaunchOptions(Side.Client, tree.RunnerPath, tree.BundlePath, tree.EngineRoot);

        var startInfo = ProcessManager.CreateStartInfo(launch, "pipe", stateFile: null, waitForDebugger: false, captureOutput: true);

        Assert.Equal(tree.RunnerPath, startInfo.FileName);
        Assert.Equal(tree.BundlePath, startInfo.WorkingDirectory);
        Assert.Contains("--bundle", startInfo.ArgumentList);
        Assert.Contains(tree.BundlePath, startInfo.ArgumentList);
        Assert.Contains("--side", startInfo.ArgumentList);
        Assert.Contains("Client", startInfo.ArgumentList);
        Assert.Contains("--engine-root", startInfo.ArgumentList);
        Assert.Contains(tree.EngineRoot, startInfo.ArgumentList);
    }

    [Fact]
    public void RepeatedLaunchRetainsBundleAndCleanupCannotEscapeIt()
    {
        using var tree = new RuntimeTree(Side.Server);
        var launch = new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, tree.BundlePath, tree.EngineRoot);
        string stateFile = Path.Combine(tree.BundlePath, "reload", "state", "state.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        File.WriteAllText(stateFile, "state");

        var initial = ProcessManager.CreateStartInfo(launch, "pipe-1", null, false, true);
        var restart = ProcessManager.CreateStartInfo(launch, "pipe-2", stateFile, false, true);

        Assert.Equal(initial.FileName, restart.FileName);
        Assert.Equal(initial.WorkingDirectory, restart.WorkingDirectory);
        Assert.Contains(tree.BundlePath, restart.ArgumentList);
        Assert.Contains(stateFile, restart.ArgumentList);

        using var manager = new ProcessManager(launch, HotReloadOptions.Default);

        manager.CleanupCompletedModuleVersions(
            System.Text.Encoding.UTF8.GetBytes(Path.Combine(tree.BundlePath, "modules.version.1")));

        Assert.True(Directory.Exists(Path.Combine(tree.BundlePath, "modules.version.1")));
    }

    [Theory]
    [InlineData("nested")]
    [InlineData("forged-top-level")]
    public void ReadyCleanup_RejectsPayloadThatIsNotTheExactResolvedModuleDirectory(string payloadKind)
    {
        using var tree = new RuntimeTree(Side.Server);
        var launch = new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, tree.BundlePath, tree.EngineRoot);
        using var manager = new ProcessManager(launch, HotReloadOptions.Default);
        string active = Path.Combine(tree.BundlePath, "modules.version.1");
        string payload = payloadKind == "nested"
            ? Path.Combine(tree.BundlePath, "nested", "modules.version.1")
            : Path.Combine(tree.BundlePath, "modules.version.forged");

        manager.CleanupCompletedModuleVersions(System.Text.Encoding.UTF8.GetBytes(payload));

        Assert.True(Directory.Exists(active));
    }

    [Fact]
    public void ReadyCleanup_RejectsAmbiguousCompletedModuleLayoutWithoutDeletingEitherVersion()
    {
        using var tree = new RuntimeTree(Side.Server);
        var launch = new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, tree.BundlePath, tree.EngineRoot);
        using var manager = new ProcessManager(launch, HotReloadOptions.Default);
        string active = Path.Combine(tree.BundlePath, "modules.version.1");
        string ambiguous = Path.Combine(tree.BundlePath, "modules.version.2");
        CompleteModuleDirectory(ambiguous);

        manager.CleanupCompletedModuleVersions(System.Text.Encoding.UTF8.GetBytes(active));

        Assert.True(Directory.Exists(active));
        Assert.True(Directory.Exists(ambiguous));
    }

    [Fact]
    public void ReadyCleanup_RejectsModuleDirectoryReplacedByLinkAfterLaunchValidation()
    {
        using var tree = new RuntimeTree(Side.Server);
        var launch = new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, tree.BundlePath, tree.EngineRoot);
        using var manager = new ProcessManager(launch, HotReloadOptions.Default);
        string active = Path.Combine(tree.BundlePath, "modules.version.1");
        string moved = Path.Combine(tree.Root, "moved-modules");
        Directory.Move(active, moved);
        try
        {
            Directory.CreateSymbolicLink(active, moved);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Directory.Move(moved, active);
            return;
        }

        try
        {
            manager.CleanupCompletedModuleVersions(System.Text.Encoding.UTF8.GetBytes(active));

            Assert.True(Directory.Exists(moved));
        }
        finally
        {
            Directory.Delete(active);
            Directory.Move(moved, active);
        }
    }

    [Theory]
    [InlineData("crlf-manifest")]
    [InlineData("unlisted-dll")]
    [InlineData("unexpected-root-file")]
    [InlineData("too-deep")]
    [InlineData("too-many-assemblies")]
    public void Constructor_RejectsNonCanonicalOrUnboundedBundleLayout(string mutation)
    {
        using var tree = new RuntimeTree(Side.Client);
        string modules = Path.Combine(tree.BundlePath, "modules.version.1");
        string manifest = Path.Combine(modules, "modules.list");
        switch (mutation)
        {
            case "crlf-manifest":
                File.WriteAllText(manifest, "Game.dll\r\n", new System.Text.UTF8Encoding(false));
                break;
            case "unlisted-dll":
                File.WriteAllText(Path.Combine(modules, "Unlisted.dll"), "unlisted");
                break;
            case "unexpected-root-file":
                File.WriteAllText(Path.Combine(tree.BundlePath, "user-owned.txt"), "user");
                break;
            case "too-deep":
                string deep = Path.Combine(tree.BundlePath, "Content");
                for (int index = 0; index <= RuntimeBundleLayout.MaxTreeDepth; index++)
                {
                    deep = Path.Combine(deep, "d");
                }
                Directory.CreateDirectory(deep);
                File.WriteAllText(Path.Combine(deep, "deep.txt"), "deep");
                break;
            case "too-many-assemblies":
                var names = new string[RuntimeBundleLayout.MaxManifestEntries + 1];
                for (int index = 0; index < names.Length; index++)
                {
                    names[index] = $"Game{index:D4}.dll";
                    File.WriteAllText(Path.Combine(modules, names[index]), "game");
                }
                File.Delete(Path.Combine(modules, "Game.dll"));
                File.WriteAllText(manifest, string.Join('\n', names) + '\n', new System.Text.UTF8Encoding(false));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Assert.Throws<InvalidDataException>(() =>
            new RuntimeLaunchOptions(Side.Client, tree.RunnerPath, tree.BundlePath, tree.EngineRoot));
    }

    [Fact]
    public void Constructor_RejectsRunnerAndBundleBelowLinkedAncestorWhenLinksAreAvailable()
    {
        using var tree = new RuntimeTree(Side.Server);
        string link = tree.Root + "-link";
        try
        {
            Directory.CreateSymbolicLink(link, tree.Root);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        try
        {
            string runner = Path.Combine(link, Path.GetRelativePath(tree.Root, tree.RunnerPath));
            string bundle = Path.Combine(link, Path.GetRelativePath(tree.Root, tree.BundlePath));

            Assert.Throws<InvalidDataException>(() => new RuntimeLaunchOptions(Side.Server, runner, bundle, tree.EngineRoot));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Theory]
    [InlineData(Side.Client)]
    [InlineData(Side.Server)]
    public void Constructor_AcceptsManifestFreeStaticBundle(Side side)
    {
        using var tree = new StaticRuntimeTree(side);

        var options = new RuntimeLaunchOptions(side, tree.RunnerPath, tree.BundlePath, tree.EngineRoot);

        Assert.Equal(tree.BundlePath, options.BundlePath);
    }

    [Fact]
    public void Constructor_RejectsStaticBundleContainingModuleStaging()
    {
        using var tree = new StaticRuntimeTree(Side.Server);
        Directory.CreateDirectory(Path.Combine(tree.BundlePath, "modules.version.1"));

        Assert.Throws<InvalidDataException>(
            () => new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, tree.BundlePath, tree.EngineRoot));
    }

    [Fact]
    public void Constructor_RejectsStaticBundleListingManagedModules()
    {
        using var tree = new StaticRuntimeTree(Side.Client);
        string nativeRoot = Path.Combine(tree.BundlePath, "runtimes", "win-x64", "native");
        Directory.CreateDirectory(nativeRoot);
        File.WriteAllText(Path.Combine(nativeRoot, "modules.list"), "Game.dll\n");

        Assert.Throws<InvalidDataException>(
            () => new RuntimeLaunchOptions(Side.Client, tree.RunnerPath, tree.BundlePath, tree.EngineRoot));
    }

    [Theory]
    [InlineData("missing-completion-marker")]
    [InlineData("wrong-side")]
    [InlineData("empty-content")]
    [InlineData("unexpected-root-entry")]
    public void Constructor_RejectsIncompleteOrForeignStaticBundleLayout(string mutation)
    {
        using var tree = new StaticRuntimeTree(Side.Server);
        switch (mutation)
        {
            case "missing-completion-marker":
                File.Delete(Path.Combine(tree.BundlePath, ".complete"));
                break;
            case "wrong-side":
                File.WriteAllText(
                    Path.Combine(tree.BundlePath, "runtime-bundle.side"),
                    $"karpik-runtime-side-v1:{Side.Client}\n");
                break;
            case "empty-content":
                File.Delete(Path.Combine(tree.BundlePath, "Content", "content.txt"));
                break;
            case "unexpected-root-entry":
                File.WriteAllText(Path.Combine(tree.BundlePath, "Game.Server.dll"), "loose managed module");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Assert.Throws<InvalidDataException>(
            () => new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, tree.BundlePath, tree.EngineRoot));
    }

    private static void CompleteModuleDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".complete"), RuntimeBundleLayout.ModuleCompletionMarker);
    }

    private sealed class StaticRuntimeTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "KarpikLaunchTests", Guid.NewGuid().ToString("N"));
        public string RunnerPath { get; }
        public string BundlePath { get; }
        public string EngineRoot { get; }

        public StaticRuntimeTree(Side side)
        {
            EngineRoot = Path.Combine(Root, "engine");
            RunnerPath = Path.Combine(EngineRoot, OperatingSystem.IsWindows() ? "runner.exe" : "runner");
            BundlePath = Path.Combine(Root, "game", "karpik-bundle");
            Directory.CreateDirectory(Path.GetDirectoryName(RunnerPath)!);
            File.WriteAllText(RunnerPath, "runner");
            Directory.CreateDirectory(Path.Combine(BundlePath, "Content"));
            File.WriteAllText(Path.Combine(BundlePath, "Content", "content.txt"), "content");
            string native = Path.Combine(BundlePath, "runtimes", "win-x64", "native");
            Directory.CreateDirectory(native);
            File.WriteAllText(Path.Combine(native, "libgame.dll"), "native");
            File.WriteAllText(Path.Combine(BundlePath, "runtime-bundle.side"), $"karpik-runtime-side-v1:{side}\n");
            File.WriteAllText(Path.Combine(BundlePath, ".complete"), "karpik-runtime-bundle-v1\n");
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class RuntimeTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "KarpikLaunchTests", Guid.NewGuid().ToString("N"));
        public string RunnerPath { get; }
        public string BundlePath { get; }
        public string EngineRoot { get; }

        public RuntimeTree(Side side)
        {
            EngineRoot = Path.Combine(Root, "engine");
            RunnerPath = Path.Combine(EngineRoot, OperatingSystem.IsWindows() ? "runner.exe" : "runner");
            BundlePath = Path.Combine(Root, "game", "karpik-bundle");
            Directory.CreateDirectory(Path.GetDirectoryName(RunnerPath)!);
            File.WriteAllText(RunnerPath, "runner");
            Directory.CreateDirectory(Path.Combine(BundlePath, "Content"));
            File.WriteAllText(Path.Combine(BundlePath, "Content", "content.txt"), "content");
            string modules = Path.Combine(BundlePath, "modules.version.1");
            Directory.CreateDirectory(modules);
            File.WriteAllText(Path.Combine(BundlePath, "runtime-bundle.side"), $"karpik-runtime-side-v1:{side}\n");
            File.WriteAllText(Path.Combine(BundlePath, ".complete"), "karpik-runtime-bundle-v1\n");
            File.WriteAllText(Path.Combine(modules, ".complete"), "karpik-module-staging-v1\n");
            File.WriteAllText(Path.Combine(modules, "modules.list"), "Game.dll\n");
            File.WriteAllText(Path.Combine(modules, "Game.dll"), "game");
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
