using Autofac;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

[Collection(nameof(WorkingDirectoryCollection))]
public sealed partial class LuaPreparationTests
{
    [Theory]
    [InlineData("systems = false", "systems")]
    [InlineData("systems = { 42 }", "systems")]
    [InlineData("systems = { [2] = {} }", "systems")]
    [InlineData("systems = { { id = 'a', phase = 'update', callback = 'run' } }", "callback")]
    [InlineData("systems = { { id = '', phase = 'update', callback = function() end } }", "id")]
    [InlineData("systems = { { id = 'a:b', phase = 'update', callback = function() end } }", "id")]
    [InlineData("systems = { { id = 'a', phase = 'fixed_update', callback = function() end } }", "phase")]
    [InlineData("systems = { { id = 'a', phase = 'update', callback = function() end, reads = { 1 } } }", "reads")]
    [InlineData("systems = { { id = 'a', phase = 'update', callback = function() end, after = 'b' } }", "after")]
    [InlineData("systems = { { id = 'a', phase = 'update', callback = function() end, typo = true } }", "typo")]
    [InlineData("systems = { { id = 'a', phase = 'update', callback = function() end }, { id = 'a', phase = 'update', callback = function() end } }", "probe:a")]
    [InlineData("systems = {}, on_update = function() end", "on_update")]
    public async Task InvalidSystemDeclarations_FailDuringLoad(string fields, string diagnostic)
    {
        await WithLuaFiles("return { " + fields + " }", null, async (loader, definition) =>
        {
            Exception error = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                using IScriptRuntime runtime = await loader.Load(definition, ExecutionSide.Server);
            });
            Assert.Contains(diagnostic, error.ToString());
            Assert.Contains("probe", error.ToString());
        });
    }

    [Theory]
    [InlineData("return { systems = {} }", "return { on_update = function() end }")]
    [InlineData("return { on_update = function() end }", "return { systems = {} }")]
    [InlineData("return { systems = { { id = 'a', phase = 'update', callback = function() end } } }", "return { systems = { { id = 'a', phase = 'update', callback = function() end } } }")]
    public async Task SystemDeclarations_ValidateAcrossSharedAndSideEntries(string shared, string server)
    {
        await WithLuaFiles(shared, server, async (loader, definition) =>
        {
            Exception error = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                using IScriptRuntime runtime = await loader.Load(definition, ExecutionSide.Server);
            });
            Assert.Contains("probe", error.ToString());
            Assert.Contains("Shared", error.ToString());
            Assert.Contains("Server", error.ToString());
        });
    }

    private static async Task WithLuaFiles(string shared, string? server,
        Func<LuaScriptLoader, ModDefinition, Task> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "karpik-lua-systems-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "Shared"));
        File.WriteAllText(Path.Combine(directory, "Shared", "main.lua"), shared);
        if (server is not null)
        {
            Directory.CreateDirectory(Path.Combine(directory, "Server"));
            File.WriteAllText(Path.Combine(directory, "Server", "main.lua"), server);
        }
        var jobs = new JobSystem(workerCount: 2, prefix: nameof(LuaPreparationTests));
        try
        {
            Job.Initialize(jobs);
            var loader = new LuaScriptLoader(new PhysicalFileSystem(), NullLogger<IScriptLoader>.Instance);
            await action(loader, new ModDefinition(new ModMetaData("probe", "Probe", "test", "1", ""), directory));
        }
        finally
        {
            Job.ShutdownIfCurrent(jobs);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealLuaPreparation_LoadsFilesystemCatalogAndRunsCallbacksExactlyOnce()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string directory = Path.Combine(Path.GetTempPath(), "karpik-lua-preparation-" + Guid.NewGuid().ToString("N"));
        string modDirectory = Path.Combine(directory, "Mods", "probe");
        Directory.CreateDirectory(Path.Combine(modDirectory, "Shared"));
        File.WriteAllText(Path.Combine(modDirectory, "mod_info.json"),
            """{"id":"probe","name":"Probe","author":"test","version":"1","description":"test"}""");
        File.WriteAllText(Path.Combine(modDirectory, "Shared", "main.lua"), """
            return {
              on_load = function() print("load") end,
              on_start = function() print("start") end,
              on_update = function(dt) assert(dt == 0.5); print("update") end,
              on_destroy = function() print("destroy") end,
              on_unload = function() print("unload") end
            }
            """);
        var jobs = new JobSystem(workerCount: 2, prefix: nameof(LuaPreparationTests));
        try
        {
            Environment.CurrentDirectory = directory;
            Job.Initialize(jobs);
            var fileSystem = new PhysicalFileSystem();
            var store = new FileContentStore(fileSystem);
            var content = new ContentRegistry(fileSystem, store);
            content.RegisterManifest(new ContentManifest(ContentManifest.CurrentSchemaVersion, []), store);
            using var assets = new AssetsManager(NullLogger<AssetsManager>.Instance, fileSystem, content, store, [], [new ModMetaDataLoader()]);
            var registry = new ModsRegistry(assets, NullLogger<IModsRegistry>.Instance);
            var logger = new ScriptLogger();
            var application = new Application(Side.Server);
            using (var lifecycle = new ModsLifecycleManager(new LuaScriptLoader(fileSystem, logger)))
            {
                await new LuaModSetPreparation(registry, application).PrepareAsync(default);
                Assert.Equal("probe", Assert.Single(registry.GetMods()).MetaData.Id);
                await new LuaSimulationPreparation(registry, lifecycle, application,
                    new SystemRegistry(), new Time(), new LuaComponentRegistry()).PrepareAsync(default);
                ModContainer container = Assert.Single(lifecycle.Containers);
                container.Start();
                container.Start();
                container.Update(0.5);
            }
            Assert.Equal(["[Mod: probe] load", "[Mod: probe] start", "[Mod: probe] update",
                "[Mod: probe] destroy", "[Mod: probe] unload"], logger.Messages);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Job.ShutdownIfCurrent(jobs);
            Directory.Delete(directory, recursive: true);
        }
    }

    internal sealed class ScriptLogger : ILogger<IScriptLoader>
    {
        public List<string> Messages { get; } = [];
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, error));
    }

    [Fact]
    public async Task Catalog_CancellationDoesNotPublishData_AndSuccessfulScanCannotBeRepeated()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string directory = Path.Combine(Path.GetTempPath(), "karpik-modset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.CurrentDirectory = directory;
            var registry = new ModsRegistry(new UnusedAssetsManager(), NullLogger<IModsRegistry>.Instance);
            Assert.Throws<InvalidOperationException>(() => registry.GetMods());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await registry.Scan(ExecutionSide.Server, cancellation.Token));
            Assert.Throws<InvalidOperationException>(() => registry.GetMods());
            await registry.Scan(ExecutionSide.Server);
            Assert.Empty(registry.GetMods());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await registry.Scan(ExecutionSide.Server));
            Assert.Empty(registry.GetMods());
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ModsRegistry_IsOwnedByModSet_AndInheritedBySimulation()
    {
        var builder = new ContainerBuilder();
        builder.Register(_ => NullLogger<IModsRegistry>.Instance).As<Microsoft.Extensions.Logging.ILogger<IModsRegistry>>();
        AttributedServiceRegistrar.Register(builder, [typeof(ModsRegistry)], ModuleScope.Engine);
        using var engine = builder.Build();
        Assert.False(engine.IsRegistered<IModsRegistry>());
        using var first = engine.BeginLifetimeScope(scope =>
        {
            AttributedServiceRegistrar.Register(scope, [typeof(ModsRegistry)], ModuleScope.ModSet);
            scope.RegisterInstance(new UnusedAssetsManager()).As<IAssetsManager>();
        });
        using var second = engine.BeginLifetimeScope(scope =>
        {
            AttributedServiceRegistrar.Register(scope, [typeof(ModsRegistry)], ModuleScope.ModSet);
            scope.RegisterInstance(new UnusedAssetsManager()).As<IAssetsManager>();
        });
        using var simulation = first.BeginLifetimeScope();
        Assert.Same(first.Resolve<IModsRegistry>(), simulation.Resolve<IModsRegistry>());
        Assert.NotSame(first.Resolve<IModsRegistry>(), second.Resolve<IModsRegistry>());
    }

    [Fact]
    public async Task PreparedModSet_CanCreateIndependentSimulationRuntimesWithoutRescanning()
    {
        var events = new List<string>();
        var registry = new Registry([Definition("a")]);
        var application = new Application(Side.Server);
        await new LuaModSetPreparation(registry, application).PrepareAsync(default);
        var first = new ModsLifecycleManager(new Loader(events));
        var second = new ModsLifecycleManager(new Loader(events));
        try
        {
            await new LuaSimulationPreparation(registry, first, application,
                new SystemRegistry(), new Time(), new LuaComponentRegistry()).PrepareAsync(default);
            await new LuaSimulationPreparation(registry, second, application,
                new SystemRegistry(), new Time(), new LuaComponentRegistry()).PrepareAsync(default);
            Assert.Equal(1, registry.Scans);
            Assert.Single(first.Containers);
            Assert.Single(second.Containers);
            Assert.NotSame(first.Containers[0], second.Containers[0]);
            Assert.Equal(["a.Load", "a.Load"], events);
        }
        finally { second.Dispose(); first.Dispose(); }
        Assert.Equal(2, events.Count(item => item == "a.Unload"));
        Assert.Equal(2, events.Count(item => item == "a.Dispose"));
    }

    [Fact]
    public async Task LoadingFailure_DisposesPendingRuntimesInReverseOrder()
    {
        var events = new List<string>();
        using var lifecycle = new ModsLifecycleManager(new Loader(events, failId: "c"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await lifecycle.Load([Definition("a"), Definition("b"), Definition("c")], ExecutionSide.Server));
        Assert.Empty(lifecycle.Containers);
        Assert.Equal(["a.Load", "b.Load", "b.Unload", "b.Dispose", "a.Unload", "a.Dispose"], events);
    }

    [Fact]
    public async Task CancellationAfterRuntimeCreation_DisposesItAndEarlierRuntimes()
    {
        var events = new List<string>();
        using var cancellation = new CancellationTokenSource();
        using var lifecycle = new ModsLifecycleManager(new Loader(events, onLoad: id =>
        {
            if (id == "b") cancellation.Cancel();
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await lifecycle.Load([Definition("a"), Definition("b")], ExecutionSide.Server, cancellation.Token));
        Assert.Empty(lifecycle.Containers);
        Assert.Equal(["a.Load", "b.Dispose", "a.Unload", "a.Dispose"], events);
    }

    private static ModDefinition Definition(string id) => new(new ModMetaData(id, id, "test", "1", ""), id);

    private sealed class Registry(ModDefinition[] definitions) : IModsRegistry
    {
        public int Scans { get; private set; }
        public JobHandle Scan(ExecutionSide side, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ExecutionSide.Server, side);
            Scans++;
            return JobHandle.Completed;
        }
        public IEnumerable<ModDefinition> GetMods()
        {
            Assert.Equal(1, Scans);
            return definitions;
        }
    }

    private sealed class Loader(List<string> events, string? failId = null, Action<string>? onLoad = null) : IScriptLoader
    {
        public JobHandle<IScriptRuntime> Load(ModDefinition definition, ExecutionSide side)
        {
            string id = definition.MetaData.Id;
            if (id == failId) return JobHandle<IScriptRuntime>.FromException(new InvalidOperationException("load failed"));
            onLoad?.Invoke(id);
            return JobHandle<IScriptRuntime>.FromResult(new Runtime(id, events));
        }
    }

    private sealed class Runtime(string id, List<string> events) : IScriptRuntime
    {
        public void Load() => events.Add(id + ".Load");
        public void Start() { }
        public void Update(double dt) { }
        public void FixedUpdate(double dt) { }
        public void Destroy() { }
        public void Unload() => events.Add(id + ".Unload");
        public void Dispose() => events.Add(id + ".Dispose");
    }

    private sealed class UnusedAssetsManager : IAssetsManager
    {
        public void Dispose() { }
        public void RegisterSaver(IAssetSaver saver) => throw new NotSupportedException();
        public void RegisterLoader(IAssetLoader loader) => throw new NotSupportedException();
        public JobHandle<AssetHandle<T>> LoadAssetAsync<T>(string path) where T : Asset => throw new NotSupportedException();
        public JobHandle<AssetHandle<Asset>> LoadAssetByPathAsync(string path) => throw new NotSupportedException();
        public JobHandle<AssetHandle<T>> SaveAssetAsync<T>(T asset, string? path = null) where T : Asset => throw new NotSupportedException();
        public bool TryAddDependency(Asset? parent, Asset? child) => throw new NotSupportedException();
        void IAssetsManager.ReleaseAsset(Asset asset) => throw new NotSupportedException();
    }
}
