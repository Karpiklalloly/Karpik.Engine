using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using Karpik.Engine.Shared.Modding.Lua.Systems;
using Karpik.Jobs;
using MoonSharp.Interpreter;
using Xunit;

public sealed partial class LuaPreparationTests
{
    [Fact]
    public void DeclaredCallback_PreservesRuntimeGuards_AndReleasesReferences()
    {
        var script = new Script();
        Table entry = script.DoString("""
            local first = true
            return { systems = { { id = 'a', phase = 'update', callback = function(dt)
              if first then first = false; reenter() end
            end } } }
            """).Table;
        using var runtime = new LuaScriptRuntime(script, [entry], "probe", ["Shared/main.lua"]);
        script.Globals.Set("reenter", DynValue.NewCallback((_, _) =>
        {
            runtime.InvokeSystem(0, 0.1);
            return DynValue.Nil;
        }));
        Assert.Contains("Recursive", Assert.ThrowsAny<Exception>(() => runtime.InvokeSystem(0, 0.1)).ToString());
        runtime.InvokeSystem(0, 0.1);
        foreach (double dt in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.InvokeSystem(0, dt));
        runtime.Dispose();
        Assert.Null(runtime.Systems[0].Callback);
        Assert.Throws<ObjectDisposedException>(() => runtime.InvokeSystem(0, 0));
    }

    [Fact]
    public void DeclaredAdapter_AddsNoAllocationsBeyondTheMoonSharpCall()
    {
        var script = new Script();
        Table entry = script.DoString("return { systems = { { id = 'a', phase = 'update', callback = function(dt) end } } }").Table;
        using var runtime = new LuaScriptRuntime(script, [entry], "probe", ["Shared/main.lua"]);
        using var owner = new ModContainer(new ModDefinition(new ModMetaData("probe", "Probe", "test", "1", ""), "."), runtime);
        owner.Load();
        owner.Start();
        var time = new Time();
        time.Update(0.001);
        ISystemUpdate adapter = new LuaUpdateSystem(owner, runtime, 0, time);
        DynValue callback = runtime.Systems[0].Callback;
        var arguments = new DynValue[1];
        for (int i = 0; i < 2000; i++)
        {
            arguments[0] = DynValue.NewNumber(time.DeltaTime);
            script.Call(callback, arguments);
            runtime.InvokeSystem(0, time.DeltaTime);
            adapter.Update();
        }
        const int iterations = 1000;
        long begin = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            arguments[0] = DynValue.NewNumber(time.DeltaTime);
            script.Call(callback, arguments);
        }
        long moonSharp = GC.GetAllocatedBytesForCurrentThread() - begin;
        begin = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) runtime.InvokeSystem(0, time.DeltaTime);
        long runtimeBytes = GC.GetAllocatedBytesForCurrentThread() - begin;
        begin = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) adapter.Update();
        long adapterBytes = GC.GetAllocatedBytesForCurrentThread() - begin;
        Assert.True(moonSharp > 0);
        Assert.Equal(moonSharp, runtimeBytes);
        Assert.Equal(moonSharp, adapterBytes);
        owner.IsEnabled = false;
        begin = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) adapter.Update();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - begin);
        owner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => adapter.Update());
    }

    [Fact]
    public void ComponentNames_AreExplicitUniqueAndClosedAfterPreparation()
    {
        var registry = CreateLuaComponents();
        registry.Register<LuaPosition>("position");
        Assert.Equal(typeof(LuaPosition), registry.Resolve("position"));
        Assert.Throws<InvalidOperationException>(() => registry.Register<LuaPosition>("position"));
        Assert.Throws<InvalidDataException>(() => registry.Resolve("missing"));
        registry.Seal();
        Assert.Throws<InvalidOperationException>(() => registry.Register<LuaPosition>("other"));
        Assert.Equal(typeof(LuaPosition), registry.Resolve("position"));
    }

    [Fact]
    public async Task DeclaredSystems_ReuseCatalogAndComponentMetadata_ButKeepSimulationStateSeparate()
    {
        await WithLuaFiles("""
            local counter = 0
            local function tick(dt) counter = counter + 1; print(counter) end
            return { systems = {
              { id = 'a', phase = 'update', callback = tick, queries = { access = { opt = { ['position'] = { readonly = false } } } } },
              { id = 'b', phase = 'update', callback = tick, queries = { access = { opt = { ['position'] = {} } } }, after = { 'probe:a' } }
            } }
            """, null, async (_, definition) =>
        {
            var logger = new ScriptLogger();
            var loader = new LuaScriptLoader(new Karpik.Engine.Core.FileSystem.PhysicalFileSystem(), logger);
            var catalog = new FixedRegistry(definition);
            var components = CreateLuaComponents();
            components.Register<LuaPosition>("position");
            var schemas = new Karpik.Engine.Shared.Modding.Bindings.ComponentSchemaRegistry();
            for (int iteration = 0; iteration < 2; iteration++)
            {
                using var lifecycle = new ModsLifecycleManager(loader);
                var registry = new RecordingRuntimeRegistry();
                using var bindings = new LuaBindingsTestScope(components, schemas);
                await bindings.Create(catalog, lifecycle, new Application(Side.Server), registry, new Time())
                    .PrepareAsync(default);
                Assert.Equal(["probe:a", "probe:b"], registry.Descriptors.Select(item => item.SystemId));
                Assert.All(registry.Descriptors, descriptor => Assert.True(descriptor.IsSequential));
                Assert.Equal(typeof(LuaPosition), Assert.Single(registry.Descriptors[0].Accesses.ToArray()).ComponentType);
                Assert.Equal(EcsAccessMode.Write, registry.Descriptors[0].Accesses.Span[0].Mode);
                Assert.Equal(EcsAccessMode.Read, registry.Descriptors[1].Accesses.Span[0].Mode);
                Assert.Equal("probe:a", Assert.Single(registry.Descriptors[1].Orders.ToArray()).TargetSystemId);
                foreach (ISystemUpdate system in registry.Systems) system.Update();
                lifecycle.Dispose();
                Assert.All(registry.Systems, system => Assert.Throws<ObjectDisposedException>(() => system.Update()));
            }
            Assert.Equal(["[Mod: probe] 1", "[Mod: probe] 2", "[Mod: probe] 1", "[Mod: probe] 2"], logger.Messages);
        });
    }

    private sealed class RecordingRuntimeRegistry : ISystemRuntimeRegistry
    {
        public List<ISystemUpdate> Systems { get; } = [];
        public List<EcsUpdateSystemDescriptor> Descriptors { get; } = [];
        public void AddInstance(string systemId, ISystem system, string layer = "BASIC_LAYER", int order = 0) => throw new NotSupportedException();
        public void AddInstance(ISystem system, EcsUpdateSystemDescriptor descriptor, string layer = "BASIC_LAYER", int order = 0)
        {
            Systems.Add((ISystemUpdate)system);
            Descriptors.Add(descriptor);
        }
    }

    [Theory]
    [InlineData(false, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(false, EcsUpdateSchedulerMode.Parallel)]
    [InlineData(true, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(true, EcsUpdateSchedulerMode.Parallel)]
    public async Task DeclaredLuaSystems_InterleaveWithNative_AndRespectLifecycle(bool useStatic, EcsUpdateSchedulerMode mode)
    {
        string source = $$"""
            local function effects(dt) assert(dt == 0.001); print('b') end
            return {
              components = { Temperature = { version = 1, fields = { Value = 'f32' } } },
              systems = {
                { id = 'a', phase = 'update', callback = function(dt, ctx)
                    assert(dt == 0.001)
                    local q = ctx.queries.access
                    for e in q:entities() do
                      local p = q.components['probe:position']:get(e)
                      q.components['probe:Temperature']:add(e).Value = 20-p.X*0.01
                    end
                    print('a')
                  end,
                  before = { 'engine:{{typeof(LuaNativeSystem).FullName}}' }, queries = { access = { inc = { ['probe:position'] = {} }, exc = { ['probe:Temperature'] = { readonly = false } } } } },
                { id = 'b', phase = 'update', callback = effects,
                  after = { 'engine:{{typeof(LuaNativeSystem).FullName}}' }, queries = { access = { opt = { ['probe:position'] = {} } } } }
              },
              on_start = function() print('start') end,
              on_destroy = function() print('destroy') end,
              on_unload = function() print('unload') end
            }
            """;
        await WithLuaFiles(source, null, async (_, definition) =>
        {
            // A second setup creates a fresh Simulation/VM from the same mod definition.
            for (int iteration = 0; iteration < 2; iteration++)
            {
                var logger = new ScriptLogger();
                var installer = new LuaProbeInstaller(definition, logger);
                var runner = new EngineRunner { UpdateSchedulerMode = mode };
                if (useStatic) runner.RegisterStaticComposition(new LuaProbeComposition(installer, logger));
                else
                {
                    runner.RegisterModule(installer);
                    runner.RegisterModule(new ModdingLuaModuleInstaller());
                }
                var mainThread = new MainThreadScheduler(Environment.CurrentManagedThreadId);
                try
                {
                    runner.Setup(new Application(Side.Server), mainThread);
                    mainThread.Execute();
                    var positions = installer.Bindings!.World.GetPool<LuaPosition>();
                    int entity = installer.Bindings.World.NewEntity(); positions.Add(entity).X = 100;
                    runner.RunGameplayFrame(0.001);
                    Assert.Equal(19, installer.Bindings.ReadTemperature(entity));
                    ModContainer owner = Assert.Single(installer.Lifecycle!.Containers);
                    owner.IsEnabled = false;
                    runner.RunGameplayFrame(0.001);
                    owner.IsEnabled = true;
                    runner.RunGameplayFrame(0.001);
                    Assert.Equal(["[Mod: probe] start", "[Mod: probe] a", "native", "[Mod: probe] b",
                        "native", "[Mod: probe] a", "native", "[Mod: probe] b"], logger.Messages);
                }
                finally { runner.Destroy(); }
                Assert.Equal(["[Mod: probe] destroy", "[Mod: probe] unload"], logger.Messages.TakeLast(2));
            }
            await Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("queries = { access = { opt = { ['missing'] = {} } } }", "missing")]
    [InlineData("after = { 'probe:missing' }", "probe:missing")]
    [InlineData("after = { 'probe:a' }", "probe:a")]
    public async Task DeclaredLuaSystems_InvalidMetadataFailsBeforeFirstFrame(string metadata, string diagnostic)
    {
        await WithLuaFiles("return { systems = { { id = 'a', phase = 'update', callback = function() end, " + metadata + " } } }",
            null, async (_, definition) =>
            {
                var installer = new LuaProbeInstaller(definition, new ScriptLogger());
                var runner = new EngineRunner();
                runner.RegisterModule(installer);
                try
                {
                    var mainThread = new MainThreadScheduler(Environment.CurrentManagedThreadId);
                    runner.Setup(new Application(Side.Server), mainThread);
                    Exception error = Assert.ThrowsAny<Exception>(() => mainThread.Execute());
                    Assert.Contains(diagnostic, error.ToString());
                }
                finally { runner.Destroy(); }
                await Task.CompletedTask;
            });
    }

    [Module(ModuleScope.Simulation)]
    private sealed class LuaProbeInstaller(ModDefinition definition, ScriptLogger logger) : IModuleInstaller
    {
        public string Name => nameof(LuaProbeInstaller);
        public ModsLifecycleManager? Lifecycle { get; private set; }
        public LuaBindingsTestScope? Bindings { get; private set; }
        public IModule CreateModule() => new LuaProbeModule();
        public void OnRegisterServices(ContainerBuilder builder)
        {
            builder.RegisterInstance(logger).AsSelf();
            builder.Register(_ => Lifecycle = new ModsLifecycleManager(new LuaScriptLoader(new Karpik.Engine.Core.FileSystem.PhysicalFileSystem(), logger)))
                .As<IModsLifecycleManager>().SingleInstance();
            var components = CreateLuaComponents();
            components.Register<LuaPosition>("probe:position");
            builder.RegisterInstance(Bindings = new LuaBindingsTestScope(components)).AsSelf();
            builder.Register(context => context.Resolve<LuaBindingsTestScope>().Create(new FixedRegistry(definition),
                context.Resolve<IModsLifecycleManager>(), context.Resolve<Application>(),
                context.Resolve<ISystemRuntimeRegistry>(), context.Resolve<Time>(), logger))
                .As<ISimulationPreparation>().SingleInstance();
        }
    }

    private sealed class FixedRegistry(ModDefinition definition) : IModsRegistry
    {
        public JobHandle Scan(ExecutionSide side, CancellationToken cancellationToken = default) => JobHandle.Completed;
        public IEnumerable<ModDefinition> GetMods() => [definition];
    }

    private sealed class LuaProbeModule : IModule
    {
        public void Add(ISystemRegistry systems)
        {
            systems.Add<LuaNativeSystem>();
        }
    }

    private sealed class LuaProbeComposition(LuaProbeInstaller installer, ScriptLogger logger) : IStaticRuntimeComposition
    {
        public void RegisterModules(IStaticModuleRegistry registry)
        {
            registry.Add(installer);
        }
        public void RegisterServices(IStaticServiceRegistry registry)
        {
            registry.Register<LuaNativeSystem, LuaNativeSystem>(ModuleScope.Simulation, ServiceLifetime.Singleton, _ => new LuaNativeSystem(logger));
        }
        public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry)
        {
            registry.AddUpdate(new Karpik.Engine.Shared.ECS.Scheduling.Generated.GeneratedEcsUpdateRegistryProvider());
        }
    }

    public struct LuaPosition : IEcsComponent { public float X; }

    [SequentialSystem]
    internal sealed class LuaNativeSystem(ScriptLogger logger) : ISystemUpdate
    {
        public void Update() => logger.Messages.Add("native");
    }
}
