using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Tooling;
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

        var separated = ParseFromInstalledRunner(tree, ["--side", "Server", "--bundle", tree.BundlePath, "--engine-root", tree.EngineRoot, "--pipe-name", "p"]);
        var nameValue = ParseFromInstalledRunner(tree, [$"--side=Server", $"--bundle={tree.BundlePath}", $"--engine-root={tree.EngineRoot}", "--pipe-name=p"]);

        Assert.Equal(Side.Server, separated.Side);
        Assert.Equal(tree.BundlePath, separated.BundlePath);
        Assert.Equal(tree.EngineRoot, separated.EngineRoot);
        Assert.Equal("p", separated.PipeName);
        Assert.Equal(separated, nameValue);
        Assert.Throws<ArgumentException>(() => RunnerLaunchArguments.Parse(["--side", "Server", $"--bundle={tree.BundlePath}", "--bundle", tree.BundlePath]));
    }

    [Fact]
    public void Parse_RejectsRunnerThatDoesNotBelongToSelectedEngineInstallation()
    {
        using var tree = new BundleTree(Side.Server);
        string foreignRunnerDirectory = Path.Combine(tree.Root, "foreign-engine", "runners", "server");
        Directory.CreateDirectory(foreignRunnerDirectory);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => RunnerLaunchArguments.Parse(
            ["--side", "Server", "--bundle", tree.BundlePath, "--engine-root", tree.EngineRoot],
            foreignRunnerDirectory));

        Assert.Contains("selected engine installation", exception.Message, StringComparison.OrdinalIgnoreCase);
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

        var parsed = ParseFromInstalledRunner(tree,
            ["--side", "Server", "--bundle", tree.BundlePath, "--engine-root", tree.EngineRoot, "--state-file", stateFile]);

        Assert.Equal(stateFile, parsed.StateFile);
        Assert.Throws<InvalidDataException>(() => ParseFromInstalledRunner(tree,
            ["--side", "Server", "--bundle", tree.BundlePath, "--engine-root", tree.EngineRoot, "--state-file", outsideState]));
        Assert.Throws<ArgumentException>(() => ParseFromInstalledRunner(tree,
            ["--side", "Server", "--bundle", tree.BundlePath, "--engine-root", tree.EngineRoot, "--state-file", "relative.bin"]));
        Assert.Throws<ArgumentException>(() => ParseFromInstalledRunner(tree,
            ["--side", "Server", "--bundle", tree.BundlePath, "--engine-root", tree.EngineRoot, "--state", "inline", "--state-file", stateFile]));
    }

    private static RunnerLaunchArguments ParseFromInstalledRunner(BundleTree tree, string[] args) =>
        RunnerLaunchArguments.Parse(args, Path.Combine(tree.EngineRoot, "runners", "server"));

    private sealed class BundleTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "KarpikRunnerArgs", Guid.NewGuid().ToString("N"));
        public string BundlePath { get; }
        public string EngineRoot { get; }

        public BundleTree(Side side)
        {
            EngineRoot = Path.Combine(Root, "engine");
            CreateEngineInstallation(EngineRoot);
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

        private static void CreateEngineInstallation(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "editor"));
            Directory.CreateDirectory(Path.Combine(root, "sdk"));
            Directory.CreateDirectory(Path.Combine(root, "runners", "client"));
            Directory.CreateDirectory(Path.Combine(root, "runners", "server"));
            Directory.CreateDirectory(Path.Combine(root, "modules", "ECS.Core"));
            Directory.CreateDirectory(Path.Combine(root, "native"));
            File.WriteAllText(Path.Combine(root, "editor", "Karpik.Editor.dll"), "editor");
            File.WriteAllText(Path.Combine(root, "sdk", "Karpik.Engine.Sdk.1.0.0.nupkg"), "sdk");
            File.WriteAllText(Path.Combine(root, "runners", "client", "Karpik.Engine.Core.Runner.dll"), "client");
            File.WriteAllText(Path.Combine(root, "runners", "server", "Karpik.Engine.Core.Runner.dll"), "server");
            File.WriteAllText(Path.Combine(root, "modules", "ECS.Core", "ECS.Core.dll"), "ecs");
            File.WriteAllText(
                Path.Combine(root, "modules", EngineModuleCatalog.FileName),
                EngineModuleCatalog.Serialize([new EngineModuleCatalogEntry("ECS.Core", EngineModuleSide.Shared)]));
            var manifest = new EngineInstallationManifest
            {
                EngineVersion = "1.0.0",
                MsBuildSdkVersion = "1.0.0",
                EditorVersion = "1.0.0",
                LayoutVersion = EngineInstallationManifest.CurrentLayoutVersion,
                RuntimeProtocolVersion = EngineInstallationManifest.CurrentRuntimeProtocolVersion,
                ContentHash = EngineContentHash.Compute(root)
            };
            File.WriteAllText(Path.Combine(root, "engine-installation.json"), manifest.ToJson());
            File.WriteAllText(Path.Combine(root, ".complete"), "complete\n");
        }
    }
}
