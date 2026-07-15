using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Xunit;

public sealed class RunnerArgumentsTests
{
    [Fact]
    public void Parse_RequiresSingleAbsoluteExistingCompleteMatchingBundle()
    {
        using var tree = new BundleTree(Side.Client);

        Assert.Throws<ArgumentException>(() => RunnerLaunchArguments.Parse(["--side", "Client"]));
        Assert.Throws<ArgumentException>(() => RunnerLaunchArguments.Parse(["--side", "Client", "--bundle", "one", "--bundle", "two"]));
        Assert.Throws<ArgumentException>(() => RunnerLaunchArguments.Parse(["--side", "Client", "--bundle", "relative"]));
        Assert.Throws<DirectoryNotFoundException>(() => RunnerLaunchArguments.Parse(["--side", "Client", "--bundle", Path.Combine(tree.Root, "missing")]));
        Assert.Throws<InvalidDataException>(() => RunnerLaunchArguments.Parse(["--side", "Server", "--bundle", tree.BundlePath]));

        File.Delete(Path.Combine(tree.BundlePath, ".complete"));
        Assert.Throws<InvalidDataException>(() => RunnerLaunchArguments.Parse(["--side", "Client", "--bundle", tree.BundlePath]));
    }

    [Fact]
    public void Parse_AcceptsSeparatedAndNameValueFormsWithoutAmbiguity()
    {
        using var tree = new BundleTree(Side.Server);

        var separated = RunnerLaunchArguments.Parse(["--side", "Server", "--bundle", tree.BundlePath, "--pipe-name", "p"]);
        var nameValue = RunnerLaunchArguments.Parse([$"--side=Server", $"--bundle={tree.BundlePath}", "--pipe-name=p"]);

        Assert.Equal(Side.Server, separated.Side);
        Assert.Equal(tree.BundlePath, separated.BundlePath);
        Assert.Equal("p", separated.PipeName);
        Assert.Equal(separated, nameValue);
        Assert.Throws<ArgumentException>(() => RunnerLaunchArguments.Parse(["--side", "Server", $"--bundle={tree.BundlePath}", "--bundle", tree.BundlePath]));
    }

    [Fact]
    public void Parse_StateFileMustBeContainedWithinBundleStateDirectory()
    {
        using var tree = new BundleTree(Side.Server);
        string stateDirectory = Path.Combine(tree.BundlePath, "reload", "state");
        Directory.CreateDirectory(stateDirectory);
        string stateFile = Path.Combine(stateDirectory, "state.bin");
        File.WriteAllText(stateFile, "state");
        string outsideState = Path.Combine(tree.Root, "outside.bin");
        File.WriteAllText(outsideState, "outside");

        var parsed = RunnerLaunchArguments.Parse(
            ["--side", "Server", "--bundle", tree.BundlePath, "--state-file", stateFile]);

        Assert.Equal(stateFile, parsed.StateFile);
        Assert.Throws<InvalidDataException>(() => RunnerLaunchArguments.Parse(
            ["--side", "Server", "--bundle", tree.BundlePath, "--state-file", outsideState]));
        Assert.Throws<ArgumentException>(() => RunnerLaunchArguments.Parse(
            ["--side", "Server", "--bundle", tree.BundlePath, "--state-file", "relative.bin"]));
        Assert.Throws<ArgumentException>(() => RunnerLaunchArguments.Parse(
            ["--side", "Server", "--bundle", tree.BundlePath, "--state", "inline", "--state-file", stateFile]));
    }

    private sealed class BundleTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "KarpikRunnerArgs", Guid.NewGuid().ToString("N"));
        public string BundlePath { get; }

        public BundleTree(Side side)
        {
            BundlePath = Path.Combine(Root, "bundle");
            string modules = Path.Combine(BundlePath, "modules.version.1");
            Directory.CreateDirectory(modules);
            Directory.CreateDirectory(Path.Combine(BundlePath, "Content"));
            File.WriteAllText(Path.Combine(BundlePath, "Content", "content.txt"), "content");
            File.WriteAllText(Path.Combine(BundlePath, ".complete"), RuntimeBundleLayout.BundleCompletionMarker);
            File.WriteAllText(Path.Combine(BundlePath, "runtime-bundle.side"), RuntimeBundleLayout.SideMarkerPrefix + side + "\n");
            File.WriteAllText(Path.Combine(modules, ".complete"), RuntimeBundleLayout.ModuleCompletionMarker);
            File.WriteAllText(Path.Combine(modules, "modules.list"), "Game.dll\n");
            File.WriteAllText(Path.Combine(modules, "Game.dll"), "game");
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
