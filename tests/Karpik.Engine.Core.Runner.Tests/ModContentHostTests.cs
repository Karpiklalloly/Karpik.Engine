using Autofac;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Content.Transport;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using Xunit;
using Microsoft.Extensions.Logging;

public sealed partial class ContentReloadHostTests
{
    [Theory]
    [InlineData(Side.Client, false)]
    [InlineData(Side.Server, false)]
    [InlineData(Side.Client, true)]
    [InlineData(Side.Server, true)]
    public async Task ModStartup_OrdinalWinnerIsTheFirstIdVersionAndLegacyRemainsBase(Side side, bool staticMode)
    {
        using var tree = new HostTree(side);
        using var initial = tree.Build(1);
        // Directory creation order is the reverse of mod priority.
        tree.BuildMod("z-winner", 3);
        tree.BuildMod("a-first", 2);
        tree.BuildMod("zz-other-side", 4, target: side == Side.Client ? AssetTarget.Server : AssetTarget.Client);
        tree.BuildMod("zzz-broken", 5, corrupt: true);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode, enabled: true, modding: true);
        try
        {
            Assert.Equal((3, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.Equal("{\"value\":1}", ReadLegacy(observation.Registry!));
            Assert.True(observation.SharedContracts);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModStartup_BeforeFinalizationOnlyLegacyIsAvailableAndEndpointIsUnbound(bool staticMode)
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode, enabled: true, modding: true);
        try
        {
            Assert.Equal("{\"value\":1}", observation.EarlyLegacy);
            Assert.False(observation.EarlyIdFound);
            Assert.True(observation.EarlyEndpointAvailable);
            Assert.Equal((1, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(Side.Client, false, false)]
    [InlineData(Side.Server, true, false)]
    [InlineData(Side.Server, false, true)]
    public async Task ModRefresh_InstallsUpdatesRemovesAndSharesTheBaseBoundary(Side side, bool staticMode, bool ipc)
    {
        using var tree = new HostTree(side);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode, enabled: ipc, modding: true);
        try
        {
            Assert.Equal((1, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            tree.BuildMod("new", 2);
            Task<ModContentRefreshResult> install = runner.RequestModContentRefreshAsync();
            Assert.Equal(ModContentRefreshStatus.Busy, (await runner.RequestModContentRefreshAsync()).Status);
            if (ipc)
            {
                using var next = tree.Build(8);
                var delivery = tree.Deliver(next.GenerationId!);
                await PumpUntilAsync(runner, Task.WhenAll(install, delivery));
                var response = await delivery;
                Assert.True(response.Outcome == Karpik.Content.Transport.ContentReloadOutcome.Applied, response.ToString());
            }
            else await PumpUntilAsync(runner, install);
            AssertApplied(await install);
            Assert.Equal((2, 2u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.Equal(ipc ? "{\"value\":8}" : "{\"value\":1}", ReadLegacy(observation.Registry!));
            Assert.Equal(1, observation.ChangedAtBegin);
            tree.BuildMod("new", 3);
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((3, 3u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            DeleteMod(tree, "new");
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((ipc ? 8 : 1, 4u), await ReadIdAsync(observation.Registry!, tree.AssetId));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("malformed")]
    [InlineData("nonordinary")]
    public async Task ModRefresh_BadInstallationRollsBackTheWholeCandidate(string damage)
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        tree.BuildMod("accepted", 2);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            tree.BuildMod("accepted", 3);
            tree.BuildMod("zz-bad", 4, corrupt: damage == "malformed");
            var path = tree.ModDirectory("zz-bad");
            if (damage == "partial") File.Delete(Path.Combine(path, "mod_info.json"));
            if (damage == "nonordinary")
            {
                var declaration = Path.Combine(path, "Content", "Server", "overrides.json");
                File.Delete(declaration);
                Directory.CreateDirectory(declaration);
            }
            Assert.Equal(ModContentRefreshStatus.Failed, (await RefreshAsync(runner)).Status);
            Assert.Equal(0UL, observation.Source!.Sequence);
            Assert.Equal((2, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModRefresh_MissingAcceptedSideFailsWithoutUninstallingMod(bool contentOnly)
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        tree.BuildMod("accepted", 2, contentOnly: contentOnly);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            // Leave the startup replacement cold to check the capture claim survived failure.
            Assert.True(observation.Registry!.TryResolve<string>(tree.AssetId, out var original));
            Directory.Delete(Path.Combine(tree.ModDirectory("accepted"), "Content", "Server"), recursive: true);
            ModContentRefreshResult refresh = await RefreshAsync(runner);
            Assert.Equal(ModContentRefreshStatus.Failed, refresh.Status);
            Assert.Contains("accepted", refresh.Error);
            Assert.Equal(0UL, observation.Source!.Sequence);
            Assert.Equal((2, original.Version), await ReadIdAsync(observation.Registry!, tree.AssetId));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ModRefresh_GameSystemReceivesLocalContractInBothCompositions(bool staticMode, bool modding)
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode, enabled: false, modding: modding);
        try
        {
            var service = Assert.IsAssignableFrom<IModContentRefresh>(observation.ModRefresh);
            if (modding) tree.BuildMod("installed", 2);
            var request = service.RequestModContentRefreshAsync();
            await PumpUntilAsync(runner, request);
            Assert.Equal(modding ? ModContentRefreshStatus.Applied : ModContentRefreshStatus.Unavailable,
                (await request).Status);
            if (modding) Assert.Equal((2, 2u), await ReadIdAsync(observation.Registry!, tree.AssetId));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModStartup_ReleasesAllWrapperClaimsAndPreservesPrimaryAndCleanupFailures()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        using var stopping = new CancellationTokenSource();
        var first = new ObservedCaptureStore("cleanup-one");
        var second = new ObservedCaptureStore("cleanup-two");
        var last = new ObservedCaptureStore();
        var runner = new EngineRunner();
        runner.RegisterModule(new HostServices(tree, new Observation()));
        runner.RegisterModule(new StartupCaptureInstaller(stopping, [MakeCapture(first), MakeCapture(second), MakeCapture(last)]));
        runner.EnableContentReload("test-host");
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        try
        {
            Task setup = runner.SetupAsync(new Application(Side.Server), scheduler, cancellationToken: stopping.Token);
            Exception error = Assert.ThrowsAny<Exception>(() => scheduler.RunUntilCompleted(setup));
            Assert.Equal((1, 1, 1), (first.Releases, second.Releases, last.Releases));
            Assert.Contains(nameof(OperationCanceledException), error.ToString());
            Assert.Contains("cleanup-one", error.ToString());
            Assert.Contains("cleanup-two", error.ToString());
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModRefresh_ReleasesAllPartialWrapperClaimsAndPreservesCaptureFailure()
    {
        using var tree = new HostTree(Side.Server);
        tree.BuildMod("a-first", 2);
        tree.BuildMod("b-next", 3);
        tree.BuildMod("z-broken", 4, corrupt: true);
        using var firstActual = await ModContentCapture.CaptureAsync(tree.ModDirectory("a-first"), "a-first", AssetTarget.Server);
        using var nextActual = await ModContentCapture.CaptureAsync(tree.ModDirectory("b-next"), "b-next", AssetTarget.Server);
        var firstStore = new ObservedCaptureStore("cleanup-first");
        var nextStore = new ObservedCaptureStore();
        var first = WithStore(firstActual!.Package, firstStore);
        var next = WithStore(nextActual!.Package, nextStore);
        var refresh = new LuaModContentRefresh(Path.Combine(tree.Root, "Mods"), AssetTarget.Server,
            new(System.Collections.Frozen.FrozenDictionary<string, ModDependencyNode>.Empty, ModDependencyResolver.Resolve([], AssetTarget.Server), []), []);
        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => refresh.CaptureAsync([first, next], _ => { }, default));
        Assert.Equal((1, 1), (firstStore.Releases, nextStore.Releases));
        Assert.Contains("z-broken", error.ToString());
        Assert.Contains("cleanup-first", error.ToString());
    }

    [Fact]
    public async Task ModRefresh_LegacyLinkedScriptDoesNotBlockOrdinaryContent()
    {
        if (!OperatingSystem.IsLinux()) return; // Link creation is non-privileged here.
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        LinkLegacyMod(tree, "linked-script");
        var observation = new Observation { LoadLua = true };
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            var owner = Assert.Single(observation.Lifecycle!.Containers);
            Assert.Equal("linked-script", owner.ModDefinition.MetaData.Id);
            tree.BuildMod("ordinary", 2);
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((2, 2u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.Same(owner, Assert.Single(observation.Lifecycle.Containers));
            Assert.DoesNotContain(observation.Logs.Messages, item => item.Message.Contains("KCR-MOD-RESTART") &&
                item.Message.Contains("linked-script"));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(Side.Client, AssetTarget.Server)]
    [InlineData(Side.Server, AssetTarget.Client)]
    public async Task ModRefresh_IgnoresOtherSideCookedContentUnderLegacyLink(Side side, AssetTarget otherSide)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var tree = new HostTree(side);
        using var initial = tree.Build(1);
        AssetId foreignAsset = tree.BuildMod("linked-script", 99, otherSide, contentOnly: false);
        string link = tree.ModDirectory("linked-script");
        string source = Path.Combine(tree.Root, "LinkedScriptSource");
        Directory.Move(link, source);
        Directory.CreateSymbolicLink(link, source);
        var observation = new Observation { LoadLua = true };
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            var owner = Assert.Single(observation.Lifecycle!.Containers);
            Assert.Equal("linked-script", owner.ModDefinition.MetaData.Id);
            Assert.Equal((1, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.False(observation.Registry!.TryResolve<string>(foreignAsset, out _));
            tree.BuildMod("ordinary", 2);
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((2, 2u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.False(observation.Registry!.TryResolve<string>(foreignAsset, out _));
            Assert.Same(owner, Assert.Single(observation.Lifecycle.Containers));
            Assert.DoesNotContain(observation.Logs.Messages, item => item.Message.Contains("linked-script") &&
                (item.Message.Contains("Cooked Content") || item.Message.Contains("KCR-MOD-RESTART")));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModRefresh_LinkedModsRootKeepsLegacyLuaIdentityAndDistinguishesRemoval()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        string source = Path.Combine(tree.Root, "LinkedModsSource");
        string actual = Path.Combine(source, "dev");
        string shared = Directory.CreateDirectory(Path.Combine(actual, "Shared")).FullName;
        File.WriteAllText(Path.Combine(actual, "mod_info.json"), """{"id":"dev"}""");
        File.WriteAllText(Path.Combine(shared, "main.lua"), "return {}");
        string logical = tree.ModDirectory("dev");
        Directory.CreateSymbolicLink(Path.GetDirectoryName(logical)!, source);
        var observation = new Observation { LoadLua = true };
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            var owner = Assert.Single(observation.Lifecycle!.Containers);
            Assert.Equal(logical, owner.ModDefinition.Directory);
            AssertApplied(await RefreshAsync(runner));
            Assert.Same(owner, Assert.Single(observation.Lifecycle.Containers));
            Assert.DoesNotContain(observation.Logs.Messages, item => item.Message.Contains("KCR-MOD-RESTART"));

            Directory.Delete(actual, recursive: true);
            AssertApplied(await RefreshAsync(runner));
            Assert.Contains(observation.Logs.Messages, item => item.Message.Contains("KCR-MOD-RESTART") && item.Message.Contains(logical));
            Assert.Same(owner, Assert.Single(observation.Lifecycle.Containers));

            File.WriteAllText(actual, "not a directory");
            Assert.Equal(ModContentRefreshStatus.Failed, (await RefreshAsync(runner)).Status);
        }
        finally { await runner.DestroyAsync(); }
    }

    private static ModContentPackage WithStore(ModContentPackage package, ObservedCaptureStore store)
    {
        var result = new ModContentPackage(package.ModId, package.Manifest, store, package.Overrides, package.SourcePath);
        var identity = typeof(ModContentPackage).GetProperty("CaptureIdentity",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        identity.SetValue(result, identity.GetValue(package));
        return result;
    }

    private static ModContentCapture MakeCapture(ObservedCaptureStore store, string id = "test") => (ModContentCapture)Activator.CreateInstance(
        typeof(ModContentCapture), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
        binder: null, args: [new ModContentPackage(id, new(ContentManifest.CurrentSchemaVersion, []), store, [], "test")], culture: null)!;

    private sealed class ObservedCaptureStore(string? error = null) : IContentStore, IContentStoreLifetime
    {
        public int Retains { get; private set; }
        public int Releases { get; private set; }
        public void Retain() => Retains++;
        public void Release()
        {
            Releases++;
            if (error is not null) throw new IOException(error);
        }
        public ReadOnlyMemory<byte> Get(string artifactLocator) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<byte>> GetAsync(string artifactLocator, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Module(ModuleScope.ModSet)]
    private sealed class StartupCaptureInstaller(CancellationTokenSource stopping, ModContentCapture[] captures) : IModuleInstaller, IModContentParticipant
    {
        public string Name => nameof(StartupCaptureInstaller);
        public void OnRegisterServices(ContainerBuilder builder) => builder.RegisterInstance(this).As<IModContentParticipant>();
        public ValueTask<(IReadOnlyList<ModContentCapture>, IReadOnlyCollection<string>)> CaptureStartupAsync(
            Action<ContentDiagnostic> report, CancellationToken cancellationToken)
        {
            stopping.Cancel();
            return ValueTask.FromResult<(IReadOnlyList<ModContentCapture>, IReadOnlyCollection<string>)>((captures, []));
        }
        public ModContentRefreshCapture? CreateRefreshCapture(Action<ContentDiagnostic> report) => null;
    }

    [Fact]
    public async Task ModRefresh_StopsAcceptedUnpublishedRequestWithoutWaitingForABoundary()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            tree.BuildMod("new", 2);
            Task<ModContentRefreshResult> request = runner.RequestModContentRefreshAsync();
            Assert.False(request.IsCompleted);
            await runner.DestroyAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ModContentRefreshStatus.Failed, (await request).Status);
            Assert.Equal(ModContentRefreshStatus.Unavailable, (await runner.RequestModContentRefreshAsync()).Status);
            Assert.Equal(0UL, observation.Source!.Sequence);
        }
        finally { if (!observation.Destroyed) await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModRefresh_UpdatesActiveScriptedContentButPreservesModSetAndSimulationOnDiskChanges()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        tree.BuildMod("script", 2, contentOnly: false);
        var observation = new Observation { LoadLua = true };
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            Assert.Equal((2, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            var owner = Assert.Single(observation.Lifecycle!.Containers);
            var definition = Assert.Single(observation.Mods!.GetMods());
            var world = observation.Bindings!.World;
            tree.BuildMod("script", 3, contentOnly: false);
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((3, 2u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            DeleteMod(tree, "script");
            tree.BuildMod("zz-new-script", 4, contentOnly: false);
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((3, 2u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            tree.BuildMod("script", 5); // The active scripted ID is now labelled content-only on disk.
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((3, 2u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.Same(owner, Assert.Single(observation.Lifecycle.Containers));
            Assert.Same(owner.Runtime, observation.Lifecycle.Containers[0].Runtime);
            Assert.Equal(definition, Assert.Single(observation.Mods.GetMods()));
            Assert.Same(world, observation.Bindings.World);
            Assert.True(observation.ChangedAtBegin > 0);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModRefresh_PreviouslyContentOnlyLayerIsRetainedIfItBecomesScripted(bool linked)
    {
        if (linked && !OperatingSystem.IsLinux()) return;
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        tree.BuildMod("patch", 2);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            if (linked)
            {
                DeleteMod(tree, "patch");
                LinkLegacyMod(tree, "patch");
            }
            else tree.BuildMod("patch", 3, contentOnly: false);
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((2, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.Contains(observation.Logs.Messages, item => item.Level == LogLevel.Warning &&
                item.Message.Contains("patch") && item.Message.Contains("restart", StringComparison.OrdinalIgnoreCase));
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public async Task ModRefresh_NewScriptedFolderSuppliesOnlyInstalledOwnerDiagnostics()
    {
        using var tree = new HostTree(Side.Server);
        using var initial = tree.Build(1);
        var observation = new Observation();
        var runner = tree.Setup(observation, staticMode: false, enabled: false, modding: true);
        try
        {
            AssetId scriptedAsset = tree.BuildMod("script-owner", 2, contentOnly: false);
            tree.BuildMod("patch", 3, targetOwner: "script-owner", targetId: scriptedAsset);
            AssertApplied(await RefreshAsync(runner));
            Assert.Equal((1, 1u), await ReadIdAsync(observation.Registry!, tree.AssetId));
            Assert.False(observation.Registry!.TryResolve<string>(scriptedAsset, out _));
            Assert.Contains(observation.Logs.Messages, item => item.Level == LogLevel.Error &&
                item.Message.Contains(scriptedAsset.ToCanonicalString(), StringComparison.OrdinalIgnoreCase));
        }
        finally { await runner.DestroyAsync(); }
    }

    private sealed class HostLog : ILoggerProvider, ILogger
    {
        internal readonly System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Message)> Messages = new();
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Enqueue((logLevel, formatter(state, exception)));
        public void Dispose() { }
    }

    private static void LinkLegacyMod(HostTree tree, string id)
    {
        string source = Path.Combine(tree.Root, "LegacySource", id);
        Directory.CreateDirectory(Path.Combine(source, "Shared"));
        File.WriteAllText(Path.Combine(source, "mod_info.json"), System.Text.Json.JsonSerializer.Serialize(new { id }));
        File.WriteAllText(Path.Combine(source, "Shared", "main.lua"), "return {}");
        string link = tree.ModDirectory(id);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateSymbolicLink(link, source);
    }

    private static void DeleteMod(HostTree tree, string id) => Directory.Delete(tree.ModDirectory(id), recursive: true);

    private static async Task<ModContentRefreshResult> RefreshAsync(EngineRunner runner)
    {
        var request = runner.RequestModContentRefreshAsync();
        await PumpUntilAsync(runner, request);
        return await request;
    }

    private static void AssertApplied(ModContentRefreshResult result) =>
        Assert.True(result.Status == ModContentRefreshStatus.Applied, result.ToString());

    private static string ReadLegacy(ContentRegistry registry)
    {
        using var stream = registry.TryOpenArtifact("game/a");
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    private static async Task<(int Value, uint Version)> ReadIdAsync(ContentRegistry registry, AssetId id)
    {
        Assert.True(registry.TryResolve<string>(id, out var reference));
        await registry.LoadAsync(reference);
        Assert.True(registry.TryGet(reference, out var lease));
        using var json = System.Text.Json.JsonDocument.Parse(lease.Payload);
        return (json.RootElement.GetProperty("value").GetInt32(), reference.Version);
    }

    private static void RegisterTestModding(EngineRunner runner, HostTree tree, Observation observation)
    {
        runner.RegisterTypes([typeof(ModsRegistry), typeof(LuaModSetPreparation), typeof(ContentRegistry), typeof(FileContentStore)]);
        runner.RegisterModule(new ModdingProbeInstaller(tree, observation));
        if (observation.LoadLua) runner.RegisterModule(new LuaObservationInstaller(observation));
    }

    private static void RegisterTestModding(IStaticServiceRegistry registry, bool modding, bool registerContent)
    {
        if (registerContent)
        {
            registry.Register<IContentStore, FileContentStore>(ModuleScope.Engine, ServiceLifetime.Singleton,
                services => new FileContentStore(services.Resolve<IFileSystem>()));
            registry.Register<FileContentStore, FileContentStore>(ModuleScope.Engine, ServiceLifetime.Singleton,
                services => new FileContentStore(services.Resolve<IFileSystem>()));
            registry.Register<ContentRegistry, ContentRegistry>(ModuleScope.Engine, ServiceLifetime.Singleton, CreateRegistry);
            registry.Register<IContentRegistry, ContentRegistry>(ModuleScope.Engine, ServiceLifetime.Singleton, CreateRegistry);
            registry.Register<IContentUpdateRegistry, ContentRegistry>(ModuleScope.Engine, ServiceLifetime.Singleton, CreateRegistry);
        }
        if (!modding) return;
        registry.Register<IModsRegistry, ModsRegistry>(ModuleScope.ModSet, ServiceLifetime.Singleton,
            services => new ModsRegistry(fileSystem: services.Resolve<IFileSystem>()));
        registry.Register<IModContentParticipant, LuaModSetPreparation>(ModuleScope.ModSet, ServiceLifetime.Singleton,
            services => new LuaModSetPreparation(services.Resolve<IModsRegistry>(), services.Resolve<Application>()));
        registry.Register<IModSetPreparation, LuaModSetPreparation>(ModuleScope.ModSet, ServiceLifetime.Singleton,
            services => new LuaModSetPreparation(services.Resolve<IModsRegistry>(), services.Resolve<Application>()));
    }

    private static ContentRegistry CreateRegistry(IServiceResolver services) => new(services.Resolve<IFileSystem>(),
        services.Resolve<IContentStore>(), deferredStartup: services.Resolve<DeferredContentStartup>());

    [Module(ModuleScope.Simulation)]
    private sealed class LuaObservationInstaller(Observation observation) : IModuleInstaller
    {
        public string Name => nameof(LuaObservationInstaller);
        public void OnRegisterServices(ContainerBuilder builder)
        {
            builder.Register(context => observation.Lifecycle = new ModsLifecycleManager(new LuaScriptLoader(
                context.Resolve<IFileSystem>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<IScriptLoader>.Instance)))
                .As<IModsLifecycleManager>().SingleInstance();
            builder.Register(_ => observation.Bindings = new LuaBindingsTestScope(new LuaComponentRegistry(
                new Karpik.Engine.Shared.Modding.Bindings.ClrComponentBindings([])))).SingleInstance();
            builder.Register(context => context.Resolve<LuaBindingsTestScope>().Create(
                observation.Mods = context.Resolve<IModsRegistry>(), context.Resolve<IModsLifecycleManager>(),
                context.Resolve<Application>(), context.Resolve<ISystemRuntimeRegistry>(), context.Resolve<Time>()))
                .As<ISimulationPreparation>();
        }
    }

    [Module(ModuleScope.ModSet)]
    private sealed class ModdingProbeInstaller(HostTree tree, Observation observation) : IModuleInstaller
    {
        public string Name => nameof(ModdingProbeInstaller);
        public void OnRegisterServices(ContainerBuilder builder) => builder.Register(context => new EarlyContentProbe(
            tree, observation, context.Resolve<ContentRegistry>())).As<IModSetPreparation>();
    }

    private sealed class EarlyContentProbe(HostTree tree, Observation observation, ContentRegistry registry) : IModSetPreparation
    {
        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            observation.EarlyLegacy = ReadLegacy(registry);
            observation.EarlyIdFound = registry.TryResolve<string>(tree.AssetId, out _);
            try
            {
                using var probe = new AsyncDisposalAdapter(new ContentReloadEndpoint(tree.Address,
                    (request, _) => Task.FromResult(new ContentReloadResponse(1, request.RequestId,
                        Karpik.Content.Transport.ContentReloadOutcome.Failed))));
                observation.EarlyEndpointAvailable = true;
            }
            catch (IOException) { }
            return ValueTask.CompletedTask;
        }
    }

    private sealed partial class HostTree
    {
        private readonly Dictionary<string, AssetId> _modAssets = [];
        public string ModDirectory(string id) => Path.Combine(Root, "Mods", id);
        public AssetId BuildMod(string id, int value, AssetTarget? target = null, bool corrupt = false, bool contentOnly = true,
            string? targetOwner = null, AssetId? targetId = null)
        {
            string directory = ModDirectory(id);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "mod_info.json"), System.Text.Json.JsonSerializer.Serialize(new { id, contentOnly }));
            if (!contentOnly)
            {
                Directory.CreateDirectory(Path.Combine(directory, "Shared"));
                File.WriteAllText(Path.Combine(directory, "Shared", "main.lua"), "return {}");
            }
            AssetTarget side = target ?? (_side == Side.Client ? AssetTarget.Client : AssetTarget.Server);
            string source = Path.Combine(Root, "ModSources", id);
            string output = Path.Combine(directory, "Content", side.ToString());
            Directory.CreateDirectory(source);
            if (!_modAssets.TryGetValue(id, out AssetId replacement)) _modAssets[id] = replacement = new AssetId(Guid.NewGuid());
            File.WriteAllText(Path.Combine(source, "mod.json.meta"), System.Text.Json.JsonSerializer.Serialize(new
            { schemaVersion = 1, assetId = replacement.ToCanonicalString(), declaredType = "raw-json", logicalName = id + "/mod", importSettings = new { }, dependencies = Array.Empty<string>() }));
            File.WriteAllText(Path.Combine(source, "mod.json"), "{\"value\":" + value + "}");
            using var result = new ContentBuildCoordinator().Build(new ContentBuildOptions
            { SourceRoot = source, OutputRoot = output, Namespace = id, Target = side, RetainGeneration = true });
            Assert.True(result.Success, string.Join(";", result.Diagnostics.Select(d => d.Message)));
            File.WriteAllText(Path.Combine(output, "overrides.json"), System.Text.Json.JsonSerializer.Serialize(new
            { schemaVersion = 1, generationId = corrupt ? "wrong" : result.GenerationId, overrides = new[] { new { targetModId = targetOwner,
                targetAssetId = (targetId ?? AssetId).ToCanonicalString(), replacementAssetId = replacement.ToCanonicalString() } } }));
            return replacement;
        }
    }
}
