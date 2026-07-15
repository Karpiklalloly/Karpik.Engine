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

        var options = new RuntimeLaunchOptions(side, tree.RunnerPath, tree.BundlePath);

        Assert.Equal(side, options.Side);
        Assert.Equal(tree.RunnerPath, options.RunnerExecutablePath);
        Assert.Equal(tree.BundlePath, options.BundlePath);
    }

    [Fact]
    public void Constructor_RejectsWrongSideIncompleteAndRelativeBundles()
    {
        using var tree = new RuntimeTree(Side.Server);

        Assert.Throws<InvalidDataException>(() => new RuntimeLaunchOptions(Side.Client, tree.RunnerPath, tree.BundlePath));
        File.Delete(Path.Combine(tree.BundlePath, ".complete"));
        Assert.Throws<InvalidDataException>(() => new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, tree.BundlePath));
        Assert.Throws<ArgumentException>(() => new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, "relative"));
    }

    [Fact]
    public void ProcessStartInfo_UsesEngineRunnerBundleWorkingDirectoryAndArgumentList()
    {
        using var tree = new RuntimeTree(Side.Client);
        var launch = new RuntimeLaunchOptions(Side.Client, tree.RunnerPath, tree.BundlePath);

        var startInfo = ProcessManager.CreateStartInfo(launch, "pipe", stateFile: null, waitForDebugger: false, captureOutput: true);

        Assert.Equal(tree.RunnerPath, startInfo.FileName);
        Assert.Equal(tree.BundlePath, startInfo.WorkingDirectory);
        Assert.Contains("--bundle", startInfo.ArgumentList);
        Assert.Contains(tree.BundlePath, startInfo.ArgumentList);
        Assert.Contains("--side", startInfo.ArgumentList);
        Assert.Contains("Client", startInfo.ArgumentList);
    }

    [Fact]
    public void RepeatedLaunchRetainsBundleAndCleanupCannotEscapeIt()
    {
        using var tree = new RuntimeTree(Side.Server);
        var launch = new RuntimeLaunchOptions(Side.Server, tree.RunnerPath, tree.BundlePath);
        string stateFile = Path.Combine(tree.BundlePath, "reload", "state", "state.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        File.WriteAllText(stateFile, "state");

        var initial = ProcessManager.CreateStartInfo(launch, "pipe-1", null, false, true);
        var restart = ProcessManager.CreateStartInfo(launch, "pipe-2", stateFile, false, true);

        Assert.Equal(initial.FileName, restart.FileName);
        Assert.Equal(initial.WorkingDirectory, restart.WorkingDirectory);
        Assert.Contains(tree.BundlePath, restart.ArgumentList);
        Assert.Contains(stateFile, restart.ArgumentList);

        string staleInside = Path.Combine(tree.BundlePath, "modules.version.2");
        string sameNameOutside = Path.Combine(tree.Root, "outside", "modules.version.2");
        CompleteModuleDirectory(staleInside);
        CompleteModuleDirectory(sameNameOutside);
        using var manager = new ProcessManager(launch, HotReloadOptions.Default);

        manager.CleanupCompletedModuleVersions(
            System.Text.Encoding.UTF8.GetBytes(Path.Combine(tree.BundlePath, "modules.version.1")));

        Assert.False(Directory.Exists(staleInside));
        Assert.True(Directory.Exists(sameNameOutside));

        static void CompleteModuleDirectory(string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, ".complete"), RuntimeBundleLayout.ModuleCompletionMarker);
        }
    }

    private sealed class RuntimeTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "KarpikLaunchTests", Guid.NewGuid().ToString("N"));
        public string RunnerPath { get; }
        public string BundlePath { get; }

        public RuntimeTree(Side side)
        {
            RunnerPath = Path.Combine(Root, "engine", OperatingSystem.IsWindows() ? "runner.exe" : "runner");
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
