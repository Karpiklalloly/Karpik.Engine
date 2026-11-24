using Autofac;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using Xunit;

public sealed partial class ContentReloadHostTests
{
    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, false)]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, true)]
    public async Task ModDependencyStartup_DependencyOrderChangesTheFirstContentWinner(Side side, bool staticMode)
    {
        using var tree = new HostTree(side);
        using var initial = tree.Build(1);
        tree.BuildMod("a-dependent", 2);
        tree.BuildMod("z-provider", 3);
        tree.DeclareStartupDependencies("z-provider", "{}");
        tree.DeclareStartupDependencies("a-dependent", """{"shared":[{"id":"z-provider","minVersion":"1.2.0"}],"client":[{"id":"missing","optional":true}],"server":[{"id":"z-provider","maxVersionExclusive":"2.0.0"}]}""");
        var runner = new EngineRunner();
        var observation = new Observation();
        try
        {
            tree.RunDependencyStartup(runner, observation, side, staticMode);
            Assert.Equal((2, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData("missing", "no payload")]
    [InlineData("wrong Lua and cooked side", "no payload")]
    [InlineData("optional mismatch", "incompatible version")]
    [InlineData("optional malformed cooked", "rejected cooked package")]
    [InlineData("optional non-directory cooked", "rejected cooked package")]
    [InlineData("cycle", "cycle")]
    public async Task ModDependencyStartup_RejectsInvalidSideGraphBeforeSimulation(string scenario, string reason)
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        // This manifest is a source even though it supplies no Lua or cooked payload of its own.
        tree.DeclareStartupDependencies("consumer", scenario.StartsWith("optional", StringComparison.Ordinal)
            ? """{"shared":[{"id":"provider","optional":true,"minVersion":"2.0.0"}]}"""
            : """{"shared":[{"id":"provider"}]}""", contentOnly: false);
        switch (scenario)
        {
            case "wrong Lua and cooked side":
                tree.BuildMod("provider", 2, AssetTarget.Client);
                tree.DeclareStartupDependencies("provider", "{}", contentOnly: false);
                tree.WriteStartupLua("provider", "Client");
                break;
            case "optional mismatch":
                tree.DeclareStartupDependencies("provider", "{}", contentOnly: false);
                tree.WriteStartupLua("provider", "Server");
                break;
            case "optional malformed cooked":
                tree.BuildMod("provider", 2, corrupt: true);
                break;
            case "optional non-directory cooked":
                tree.DeclareStartupDependencies("provider", "{}");
                string sideDirectory = Path.Combine(tree.ModDirectory("provider"), "Content", "Server");
                Directory.CreateDirectory(Path.GetDirectoryName(sideDirectory)!);
                File.WriteAllText(sideDirectory, "not a package directory");
                break;
            case "cycle":
                tree.DeclareStartupDependencies("provider", """{"server":[{"id":"consumer","optional":true}]}""", contentOnly: false);
                tree.WriteStartupLua("consumer", "Shared");
                tree.WriteStartupLua("provider", "Server");
                break;
        }
        var observation = new Observation { LoadLua = true };
        var runner = new EngineRunner();
        try
        {
            var error = Assert.Throws<ModDependencyException>(() => tree.RunDependencyStartup(runner, observation, Side.Server, staticMode: false));
            Assert.Contains("Server", error.Message);
            Assert.Contains("consumer", error.Message);
            Assert.Contains("provider", error.Message);
            Assert.Contains(reason, error.Message);
            Assert.Null(observation.Lifecycle);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(Side.Client, false, false)]
    [InlineData(Side.Server, false, true)]
    [InlineData(Side.Client, true, true)]
    [InlineData(Side.Server, true, false)]
    public async Task ModDependencyStartup_RejectsCookedParticipantNotAdmittedByComposition(Side side, bool staticMode, bool rejectedIsSource)
    {
        using var tree = new HostTree(side);
        using var initial = tree.Build(1);
        tree.BuildStartupCollision("colliding");
        var observation = new Observation { LoadLua = true };
        var runner = new EngineRunner();
        if (rejectedIsSource)
        {
            tree.DeclareStartupDependencies("colliding", """{"shared":[{"id":"lua"}]}""");
            tree.DeclareStartupDependencies("lua", "{}", contentOnly: false);
        }
        else tree.DeclareStartupDependencies("lua", """{"shared":[{"id":"colliding","optional":true}]}""", contentOnly: false);
        tree.WriteStartupLua("lua", "Shared");
        try
        {
            var error = Assert.Throws<ModDependencyException>(() => tree.RunDependencyStartup(runner, observation, side, staticMode));
            Assert.Contains(side.ToString(), error.Message);
            Assert.Contains("colliding", error.Message);
            Assert.Contains("Content", error.Message);
            Assert.Null(observation.Lifecycle);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModDependencyStartup_RejectsDisallowedLinkedCookedButAcceptsLinkedLuaOnTheActiveSide()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        tree.BuildMod("z-provider", 2);
        tree.DeclareStartupDependencies("z-provider", "{}", contentOnly: false);
        tree.DeclareStartupDependencies("a-consumer", """{"shared":[{"id":"z-provider","optional":true}]}""", contentOnly: false);
        tree.WriteStartupLua("a-consumer", "Shared");
        string provider = tree.ModDirectory("z-provider");
        string source = Path.Combine(tree.Root, "LinkedProvider");
        Directory.Move(provider, source);
        Directory.CreateSymbolicLink(provider, source);
        tree.WriteStartupLua("z-provider", "Client", "error('wrong side must not run')");
        var runner = new EngineRunner();
        var observation = new Observation { LoadLua = true };
        try
        {
            var error = Assert.Throws<ModDependencyException>(() => tree.RunDependencyStartup(runner, observation, Side.Server, staticMode: false));
            Assert.Contains("rejected cooked package", error.Message);
            Assert.Null(observation.Lifecycle);

            string shared = Directory.CreateDirectory(Path.Combine(source, "Shared")).FullName;
            File.CreateSymbolicLink(Path.Combine(shared, "main.lua"), Path.Combine(source, "linked-entry.lua"));
            File.WriteAllText(Path.Combine(source, "linked-entry.lua"), "return {}");
            tree.RunDependencyStartup(runner, observation, Side.Server, staticMode: false, register: false);
            Assert.Equal(["z-provider", "a-consumer"], observation.Mods!.GetMods().Select(static mod => mod.MetaData.Id));
            Assert.Equal((1, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, false)]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, true)]
    public async Task ModDependencyStartup_LuaOnlyResolvesBeforeSimulationWithoutUsingCooked(Side side, bool staticMode)
    {
        using var tree = new HostTree(side);
        tree.BuildMod("z-provider", 2);
        tree.DeclareStartupDependencies("z-provider", "{}", contentOnly: false);
        tree.DeclareStartupDependencies("a-consumer", """{"shared":[{"id":"z-provider"}]}""", contentOnly: false);
        tree.WriteStartupLua("a-consumer", "Shared");
        var observation = new Observation { LoadLua = true };
        var runner = new EngineRunner();
        try
        {
            var error = Assert.Throws<ModDependencyException>(() => tree.RunDependencyStartup(runner, observation, side, staticMode, luaOnly: true));
            Assert.Contains("no payload", error.Message);
            Assert.Null(observation.Lifecycle);

            tree.WriteStartupLua("z-provider", side.ToString());
            tree.RunDependencyStartup(runner, observation, side, staticMode, luaOnly: true, register: false);
            Assert.Equal(["z-provider", "a-consumer"], observation.Mods!.GetMods().Select(static mod => mod.MetaData.Id));
            Assert.Null(observation.Registry);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModDependencyStartup_ValidationRunsBeforePublishingIdsOrRetainingStoresAndReleasesEveryCapture()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var first = new ObservedCaptureStore();
        var second = new ObservedCaptureStore();
        var validation = new RejectStartupCatalog(tree.AssetId, [MakeCapture(first, "first"), MakeCapture(second, "second")]);
        var runner = new EngineRunner();
        runner.RegisterModule(validation);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => tree.RunDependencyStartup(runner, new Observation(), Side.Server, staticMode: false));
            Assert.Equal("Reject test catalog", error.Message);
            Assert.Equal((0, 0), (first.Retains, second.Retains));
            Assert.Equal((1, 1), (first.Releases, second.Releases));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Module(ModuleScope.ModSet)]
    private sealed class RejectStartupCatalog(AssetId assetId, ModContentCapture[] captures) : IModuleInstaller, IModContentParticipant
    {
        private ContentRegistry? _registry;
        public string Name => nameof(RejectStartupCatalog);
        public void OnRegisterServices(ContainerBuilder builder) => builder.Register(context =>
        {
            _registry = context.Resolve<ContentRegistry>();
            return this;
        }).As<IModContentParticipant>();
        public ValueTask<(IReadOnlyList<ModContentCapture>, IReadOnlyCollection<string>)> CaptureStartupAsync(
            Action<ContentDiagnostic> report, CancellationToken cancellationToken) =>
            ValueTask.FromResult<(IReadOnlyList<ModContentCapture>, IReadOnlyCollection<string>)>((captures, []));
        public ModContentRefreshCapture? CreateRefreshCapture(Action<ContentDiagnostic> report) => null;
        public void ValidateStartupPackages(IReadOnlyList<ModContentPackage> packages, CancellationToken cancellationToken)
        {
            Assert.Equal(captures.Select(static capture => capture.Package), packages);
            Assert.False(_registry!.TryResolve<string>(assetId, out _));
            Assert.Equal("{\"value\":1}", ReadLegacy(_registry));
            throw new InvalidOperationException("Reject test catalog");
        }
    }

    private sealed class LuaOnlyStartupComposition(HostTree tree, Observation observation) : IStaticRuntimeComposition
    {
        public void RegisterModules(IStaticModuleRegistry registry)
        {
            registry.Add(new HostServices(tree, observation));
            registry.Add(new ObserverModule());
            registry.Add(new LuaObservationInstaller(observation));
        }
        public void RegisterServices(IStaticServiceRegistry registry) => RegisterTestModding(registry, modding: true, registerContent: false);
        public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry) { }
    }

    private sealed partial class HostTree
    {
        public void DeclareStartupDependencies(string id, string dependencies, bool contentOnly = true)
        {
            string directory = Directory.CreateDirectory(ModDirectory(id)).FullName;
            string metadata = $$"""{"id":"{{id}}","version":"1.2.0","contentOnly":{{(contentOnly ? "true" : "false")}},"dependencies":{{dependencies}}}""";
            File.WriteAllText(Path.Combine(directory, "mod_info.json"), metadata);
        }

        public void WriteStartupLua(string id, string side, string code = "return {}") => File.WriteAllText(
            Path.Combine(Directory.CreateDirectory(Path.Combine(ModDirectory(id), side)).FullName, "main.lua"), code);

        public void BuildStartupCollision(string id)
        {
            _modAssets.Add(id, AssetId);
            BuildMod(id, 2);
            File.Delete(Path.Combine(ModDirectory(id), "Content", _side.ToString(), "overrides.json"));
        }

        public void RunDependencyStartup(EngineRunner runner, Observation observation, Side side, bool staticMode, bool luaOnly = false,
            bool register = true, MainThreadScheduler? scheduler = null)
        {
            if (register)
            {
                if (staticMode)
                    runner.RegisterStaticComposition(luaOnly ? new LuaOnlyStartupComposition(this, observation)
                        : new Composition(this, observation, modding: true, registerContent: true));
                else
                {
                    runner.RegisterModule(new HostServices(this, observation));
                    runner.RegisterModule(new ObserverModule());
                    if (luaOnly)
                    {
                        runner.RegisterTypes([typeof(ModsRegistry), typeof(LuaModSetPreparation)]);
                        runner.RegisterModule(new LuaObservationInstaller(observation));
                    }
                    else RegisterTestModding(runner, this, observation);
                }
            }
            scheduler ??= new MainThreadScheduler(Environment.CurrentManagedThreadId);
            scheduler.RunUntilCompleted(runner.SetupAsync(new Application(side), scheduler));
        }
    }
}
