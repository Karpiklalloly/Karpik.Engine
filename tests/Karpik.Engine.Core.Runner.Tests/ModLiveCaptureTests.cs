using Karpik.Content.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Collection(nameof(WorkingDirectoryCollection))]
public sealed class ModLiveCaptureTests
{
    [Fact]
    public async Task StartupPinsEntryPointAndLazyImportsBeforeAnyVmIsLoaded()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var tree = new ModTree();
        tree.Write("probe", "mod_info.json", """{"id":"probe"}""");
        tree.Write("probe", "Shared/main.lua", """
            return { on_start = function() assert(require('late') == 'accepted') end }
            """);
        string helper = tree.Write("probe", "Shared/late.lua", "return 'accepted'");
        tree.Write("probe", "Client/main.lua", "error('other side')");
        var registry = await tree.StartupAsync(Side.Server);

        tree.Write("probe", "Shared/main.lua", "error('edited after capture')");
        tree.Write("probe", "Server/late.lua", "return 'new side file'");
        File.Delete(helper);

        await tree.StartAsync(Assert.Single(registry.GetMods()), ExecutionSide.Server);
    }

    [Fact]
    public async Task LazyImportCannotSeeNewSameSideFileAfterCapture()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var tree = new ModTree();
        tree.Write("probe", "mod_info.json", """{"id":"probe"}""");
        tree.Write("probe", "Shared/main.lua", """
            return { on_start = function()
                local ok = pcall(require, 'late')
                assert(not ok, 'new module leaked into captured source')
            end }
            """);
        var registry = await tree.StartupAsync(Side.Server);
        tree.Write("probe", "Server/late.lua", "return 'not accepted'");

        await tree.StartAsync(Assert.Single(registry.GetMods()), ExecutionSide.Server);
    }

    [Fact]
    public async Task StartupRejectsAnOversizedLazyModuleBeforePublication()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var tree = new ModTree();
        tree.Write("probe", "mod_info.json", """{"id":"probe"}""");
        tree.Write("probe", "Shared/main.lua", "return {}");
        string helper = tree.Write("probe", "Server/helper.lua", "");
        using (var stream = File.OpenWrite(helper)) stream.SetLength(64 * 1024 * 1024);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => tree.StartupAsync(Side.Server));

        Assert.Contains("helper.lua", error.Message);
        Assert.Contains("exceeds", error.Message);
    }

    [Fact]
    public async Task StartupRejectsSharedImportLinkToTheOtherSideEvenWhenNeverInvoked()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var tree = new ModTree();
        tree.Write("probe", "mod_info.json", """{"id":"probe"}""");
        string entry = tree.Write("probe", "Shared/main.lua", "return {}");
        string privateFile = tree.Write("probe", "Client/private.lua", "return 'client'");
        File.CreateSymbolicLink(Path.Combine(Path.GetDirectoryName(entry)!, "private.lua"), privateFile);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => tree.StartupAsync(Side.Server));

        Assert.Contains("private.lua", error.Message);
    }

    [Theory]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, false)]
    public async Task ExplicitSelectionUsesAcceptedSurvivorsAndFreshNewPackages(Side side, bool client)
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var tree = new ModTree();
        string config = tree.WriteRoot("mods.json", """{"disabled":["provider"]}""");
        tree.Write("consumer", "mod_info.json", """{"id":"consumer","version":"1.0.0","dependencies":{"client":[{"id":"provider","optional":true,"minVersion":"2.0.0"}]}}""");
        tree.Write("consumer", "Shared/main.lua", "return {}");
        tree.Write("provider", "mod_info.json", """{"id":"provider","version":"1.0.0"}""");
        tree.Write("provider", "Client/main.lua", "error('initial disabled copy')");
        var registry = await tree.StartupAsync(side);
        var basis = registry.StartupDependencies!;
        var oldConsumer = Assert.Single(basis.ScriptedMods);
        tree.Write("consumer", "mod_info.json", """{"id":"consumer","version":"99.0.0","dependencies":null}""");
        tree.Write("consumer", "Shared/main.lua", "error('do not reopen the survivor')");
        tree.Write("provider", "mod_info.json", """{"id":"provider","version":"2.0.0"}""");
        tree.Write("provider", "Client/main.lua", "return {}");

        var selection = registry.DiscoverSelection(basis, ["consumer", "provider"]);
        var candidate = ModsRegistry.ComposeSelection(selection, []);
        var consumer = candidate.PackagesById["consumer"].Definition;
        var provider = candidate.PackagesById["provider"].Definition;
        Assert.Same(oldConsumer.ScriptSource, consumer.ScriptSource);
        Assert.Equal("1.0.0", consumer.MetaData.Version);
        Assert.Equal("2.0.0", provider.MetaData.Version);
        Assert.Equal(client, consumer.ResolvedDirectDependencies!.Contains("provider"));
        Assert.Equal(client, candidate.NodesById["provider"].HasScriptPayload);
        if (client) await tree.StartAsync(provider, ExecutionSide.Client);
        var removed = ModsRegistry.ComposeSelection(registry.DiscoverSelection(candidate, ["consumer"]), []);
        Assert.Empty(Assert.Single(removed.ScriptedMods).ResolvedDirectDependencies!);
        Assert.Same(oldConsumer.ScriptSource, Assert.Single(removed.ScriptedMods).ScriptSource);
        Assert.Same(basis, registry.StartupDependencies);
        Assert.Equal("""{"disabled":["provider"]}""", File.ReadAllText(config));
    }

    [Fact]
    public async Task ExplicitSelectionRejectsUnsatisfiedDependencyWithoutAutoEnabling()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var tree = new ModTree();
        tree.WriteRoot("mods.json", """{"disabled":["consumer","provider"]}""");
        tree.Write("consumer", "mod_info.json", """{"id":"consumer","dependencies":{"shared":[{"id":"provider"}]}}""");
        tree.Write("consumer", "Shared/main.lua", "return {}");
        tree.Write("provider", "mod_info.json", """{"id":"provider"}""");
        tree.Write("provider", "Shared/main.lua", "return {}");
        var registry = await tree.StartupAsync(Side.Server);
        var basis = registry.StartupDependencies!;
        var selection = registry.DiscoverSelection(basis, ["consumer"]);

        var failure = Assert.Throws<ModDependencyException>(() => ModsRegistry.ComposeSelection(selection, []));

        Assert.Contains("provider", failure.Message);
        Assert.Same(basis, registry.StartupDependencies);
        Assert.Empty(registry.GetMods());
    }

    [Fact]
    public async Task SelectionRejectsAnEntryPointThatDisappearsBeforeComposition()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var tree = new ModTree();
        tree.WriteRoot("mods.json", """{"disabled":["new"]}""");
        tree.Write("new", "mod_info.json", """{"id":"new"}""");
        string entry = tree.Write("new", "Shared/main.lua", "return {}");
        var registry = await tree.StartupAsync(Side.Server);
        var basis = registry.StartupDependencies!;
        var selection = registry.DiscoverSelection(basis, ["new"]);
        File.Delete(entry);

        var failure = Assert.Throws<InvalidDataException>(() => ModsRegistry.ComposeSelection(selection, []));

        Assert.Contains("main.lua", failure.Message);
        Assert.Same(basis, registry.StartupDependencies);
    }

    [Fact]
    public async Task SharedIdentityIsIndependentOfSideIdentity()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var tree = new ModTree();
        tree.Write("probe", "mod_info.json", """{"id":"probe"}""");
        tree.Write("probe", "Shared/main.lua", "return {}");
        tree.Write("probe", "Client/main.lua", "return { on_load = function() end }");
        tree.Write("probe", "Server/main.lua", "return {}");
        var client = Assert.Single((await tree.StartupAsync(Side.Client)).GetMods()).ScriptSource!;
        var server = Assert.Single((await tree.StartupAsync(Side.Server)).GetMods()).ScriptSource!;

        Assert.Equal(client.SharedIdentity, server.SharedIdentity);
        Assert.NotEqual(client.SideIdentity, server.SideIdentity);
        Assert.False(client.TryGetFile("Server/main.lua", out _));
        Assert.False(server.TryGetFile("Client/main.lua", out _));
    }

    [Fact]
    public void CaptureRejectsSameLengthMutationBetweenObservations()
    {
        if (!LuaSourceCapture.IsSupported) return;
        using var tree = new ModTree();
        string file = tree.Write("probe", "Shared/main.lua", "return 'first'");
        string root = Path.GetDirectoryName(Path.GetDirectoryName(file))!;
        var originalWriteTime = File.GetLastWriteTimeUtc(file);

        var failure = Assert.Throws<InvalidDataException>(() => LuaSourceCapture.Capture(root, ExecutionSide.Server,
            verifyManifest: _ =>
            {
                File.WriteAllText(file, "return 'other'");
                File.SetLastWriteTimeUtc(file, originalWriteTime);
            }));

        Assert.Contains("changed or disappeared", failure.Message);
    }

    private sealed class ModTree : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("karpik-live-capture-").FullName;
        private readonly JobSystem _jobs = new(workerCount: 2, prefix: nameof(ModLiveCaptureTests));
        private readonly PhysicalFileSystem _fileSystem;

        public ModTree()
        {
            string previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = _root;
                _fileSystem = new PhysicalFileSystem();
            }
            finally { Environment.CurrentDirectory = previous; }
            Job.Initialize(_jobs);
        }

        public string WriteRoot(string relative, string text)
        {
            string path = Path.Combine(_root, relative);
            File.WriteAllText(path, text);
            return path;
        }

        public string Write(string mod, string relative, string text)
        {
            string path = Path.Combine(_root, "Mods", mod, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        public async Task<ModsRegistry> StartupAsync(Side side)
        {
            var registry = new ModsRegistry(fileSystem: _fileSystem);
            var preparation = new LuaModSetPreparation(registry, new Application(side));
            await preparation.PrepareAsync(default);
            preparation.CompleteStartupWithoutContent(default);
            return registry;
        }

        public async Task StartAsync(ModDefinition definition, ExecutionSide side)
        {
            var loader = new LuaScriptLoader(_fileSystem, NullLogger<IScriptLoader>.Instance);
            using var owner = new ModContainer(definition, await loader.Load(definition, side));
            owner.Load();
            owner.Start();
        }

        public void Dispose()
        {
            Job.ShutdownIfCurrent(_jobs);
            Directory.Delete(_root, recursive: true);
        }
    }
}
