using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed partial class LuaPreparationTests
{
    [Theory]
    [InlineData(false, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(false, EcsUpdateSchedulerMode.Parallel)]
    [InlineData(true, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(true, EcsUpdateSchedulerMode.Parallel)]
    public async Task LuaStructuralCommand_RealRunnerAppliesBeforeNativeAndNextLua(
        bool useStatic, EcsUpdateSchedulerMode mode)
    {
        string source = """
            local invocation=0
            return {
              components={Temperature={version=1,fields={Value='f32'}}},
              systems={
                {id='spawn',phase='update',before={'engine:__NATIVE_OBSERVER__'},
                 queries={q={opt={['probe:position']={readonly=false},['probe:Temperature']={readonly=false}}}},
                 callback=function(dt,ctx)
                    invocation=invocation+1
                    local q=ctx.queries.q
                    local p=q.components['probe:position']; local t=q.components['probe:Temperature']
                    local s=p:new_snapshot(); s.X=10
                    local heat=t:new_snapshot(); heat.Value=5
                    local pending=ctx.commands:create()
                    ctx.commands:add(p,pending,s); ctx.commands:add(t,pending,heat)
                    s.X=999; heat.Value=999
                    local count=0; for e in q:entities() do count=count+1 end
                    assert(count==invocation-1)
                    print('spawn:'..invocation)
                 end},
                {id='observe',phase='update',after={'engine:__NATIVE_OBSERVER__'},
                 queries={q={inc={['probe:position']={},['probe:Temperature']={}}}},
                 callback=function(dt,ctx)
                    local q=ctx.queries.q; local count=0
                    for e in q:entities() do
                      count=count+1
                      assert(q.components['probe:position']:get(e).X==110)
                      assert(q.components['probe:Temperature']:get(e).Value==5)
                    end
                    assert(count==invocation)
                    print('observe:'..count)
                 end}
              },
              on_destroy=function() print('destroy') end,
              on_unload=function() print('unload') end
            }
            """.Replace("__NATIVE_OBSERVER__", typeof(StructuralNativeObserver).FullName!, StringComparison.Ordinal);
        await WithLuaFiles(source, null, (_, definition) =>
        {
            // Each runner owns a fresh Simulation and VM; no pending or command state may leak.
            for (int simulation = 0; simulation < 2; simulation++)
            {
                var logger = new ScriptLogger();
                var installer = new StructuralRunnerInstaller(definition, logger);
                var runner = new EngineRunner(NullLoggerFactory.Instance) { UpdateSchedulerMode = mode };
                if (useStatic) runner.RegisterStaticComposition(new StructuralRunnerComposition(installer));
                else
                {
                    runner.RegisterModule(installer);
                    runner.RegisterModule(new ModdingLuaModuleInstaller());
                }
                try
                {
                    var mainThread = new MainThreadScheduler(Environment.CurrentManagedThreadId);
                    runner.Setup(new Application(Side.Server), mainThread); mainThread.Execute();
                    Assert.Equal(0, installer.Bindings!.World.Count);
                    for (int frame = 1; frame <= 2; frame++)
                    {
                        runner.RunGameplayFrame(.02);
                        Assert.Equal(frame, installer.Bindings.World.Count);
                    }
                    Assert.Equal(new[] { "[Mod: probe] spawn:1", "native:1", "[Mod: probe] observe:1",
                        "[Mod: probe] spawn:2", "native:2", "[Mod: probe] observe:2" }, logger.Messages);
                    AssertCommandAccessesWithoutSequentialMask(installer.RuntimeRegistry!.Descriptors);
                }
                finally { runner.Destroy(); }
                Assert.Equal(new[] { "[Mod: probe] destroy", "[Mod: probe] unload" }, logger.Messages.TakeLast(2));
            }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(EcsUpdateSchedulerMode.Deterministic, false)]
    [InlineData(EcsUpdateSchedulerMode.Parallel, false)]
    [InlineData(EcsUpdateSchedulerMode.Deterministic, true)]
    [InlineData(EcsUpdateSchedulerMode.Parallel, true)]
    public void LuaStructuralCommand_FatalPlaybackStopsLaterSystemsEvenWithLogAndContinue(
        EcsUpdateSchedulerMode mode, bool mutateReentrantly)
    {
        var script = new MoonSharp.Interpreter.Script();
        using var runtime = new LuaScriptRuntime(script, [script.DoString("""
            return {systems={
              {id='spawn',phase='update',queries={q={opt={['probe:position']={readonly=false}}}},
               callback=function(dt,ctx)
                 local entity=ctx.commands:create()
                 ctx.commands:add(ctx.queries.q.components['probe:position'],entity)
               end},
              {id='later',phase='update',after={'probe:spawn'},callback=function() later=true end}
            }}
            """).Table], "probe", ["Shared/main.lua"]);
        var world = new EcsDefaultWorld();
        using var scope = new ComponentCallbackScope();
        var components = CreateLuaComponents(); components.Register<LuaPosition>("probe:position");
        var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze(runtime.Components);
        using var preparer = new ComponentQueryPreparer(world, components.Bindings, schemas, scope);
        using var scheduler = new EcsUpdateScheduler();
        using var owner = new ModContainer(new ModDefinition(new ModMetaData("probe", "Probe", "test", "1", ""), "."), runtime);
        var listener = new StructuralOnAddFaultListener(world, mutateReentrantly);
        try
        {
            preparer.Initialize();
            runtime.PrepareEcs(preparer, scope, new ScriptSystemOptions { ErrorPolicy = ScriptSystemErrorPolicy.LogAndContinue }, NullLogger.Instance);
            owner.Load(); owner.Start();
            world.GetPool<LuaPosition>().AddListener(listener);
            var time = new Time(); time.Update(.02);
            var native = new StructuralAfterFailureObserver();
            ISystemUpdate[] systems = [
                new Karpik.Engine.Shared.Modding.Lua.Systems.LuaUpdateSystem(owner, runtime, 0, time),
                native,
                new Karpik.Engine.Shared.Modding.Lua.Systems.LuaUpdateSystem(owner, runtime, 1, time)];
            EcsUpdateSystemDescriptor[] descriptors = [
                new(typeof(Karpik.Engine.Shared.Modding.Lua.Systems.LuaUpdateSystem), true,
                    runtime.GetContext(0).Accesses, runtime.Systems[0].Orders, "probe:spawn"),
                new(typeof(StructuralAfterFailureObserver), true, Array.Empty<EcsComponentAccessDescriptor>(),
                    new EcsSystemOrderDescriptor[] { new("probe:spawn", EcsOrderKind.After), new("probe:later", EcsOrderKind.Before) }, "test:native"),
                new(typeof(Karpik.Engine.Shared.Modding.Lua.Systems.LuaUpdateSystem), true,
                    runtime.GetContext(1).Accesses, runtime.Systems[1].Orders, "probe:later")];
            scheduler.InitializeInstances(systems, descriptors, mode);
            var error = Assert.Throws<StructuralCommandPlaybackException>(scheduler.Update);
            Assert.NotNull(error.InnerException);
            if (!mutateReentrantly) Assert.Contains("injected native OnAdd failure", error.InnerException.Message);
            Assert.Equal(0, native.Runs);
            Assert.True(script.Globals.Get("later").IsNil());
        }
        finally
        {
            world.GetPool<LuaPosition>().RemoveListener(listener);
            // A failed playback may leave partial state. Recover by destroying the Simulation, not retrying it.
            owner.Dispose(); preparer.Dispose(); scope.Dispose(); world.Destroy();
        }
    }

    private sealed class StructuralOnAddFaultListener(EcsDefaultWorld world, bool mutateReentrantly) : IEcsPoolEventListener
    {
        public void OnAdd(int entityID)
        {
            if (mutateReentrantly) world.NewEntity();
            else throw new InvalidOperationException("injected native OnAdd failure");
        }
        public void OnGet(int entityID) { }
        public void OnDel(int entityID) { }
    }

    [RuntimeSystem, SequentialSystem]
    private sealed class StructuralAfterFailureObserver : ISystemUpdate
    {
        public int Runs { get; private set; }
        public void Update() => Runs++;
    }

    private static void AssertCommandAccessesWithoutSequentialMask(List<EcsUpdateSystemDescriptor> recorded)
    {
        EcsUpdateSystemDescriptor spawn = Assert.Single(recorded, d => d.SystemId == "probe:spawn");
        EcsUpdateSystemDescriptor observe = Assert.Single(recorded, d => d.SystemId == "probe:observe");
        Assert.Contains(spawn.Accesses.ToArray(), a => a.Key.ClrType == typeof(LuaPosition) && a.Mode == EcsAccessMode.Write);
        Assert.Contains(spawn.Accesses.ToArray(), a => a.Key.DynamicId == "probe:Temperature" && a.Mode == EcsAccessMode.Write);
        Assert.Contains(spawn.Accesses.ToArray(), a => a.Key.ClrType == typeof(DynamicComponentAnchor) && a.Mode == EcsAccessMode.Write);
        Assert.All(observe.Accesses.ToArray(), a => Assert.Equal(EcsAccessMode.Read, a.Mode));
        var graph = EcsUpdateGraphBuilder.BuildInstances([
            new(spawn.SystemType, false, spawn.Accesses, Array.Empty<EcsSystemOrderDescriptor>(), "synthetic:spawn"),
            new(observe.SystemType, false, observe.Accesses, Array.Empty<EcsSystemOrderDescriptor>(), "synthetic:observe")]);
        Assert.Contains(0, graph.Nodes.Span[1].DependencySystemIds.ToArray());
        Assert.All(graph.Nodes.ToArray(), node => Assert.False(node.IsSequential));
    }

    [Module(ModuleScope.Simulation)]
    private sealed class StructuralRunnerInstaller(ModDefinition definition, ScriptLogger logger) : IModuleInstaller
    {
        public string Name => nameof(StructuralRunnerInstaller);
        public LuaBindingsTestScope? Bindings { get; private set; }
        public RecordingRuntimeRegistry? RuntimeRegistry { get; private set; }
        public IModule CreateModule() => new StructuralRunnerModule();
        public StructuralNativeObserver CreateNative() => new StructuralNativeObserver(Bindings!, logger);
        public void OnRegisterServices(ContainerBuilder builder)
        {
            builder.RegisterInstance(logger).AsSelf();
            var components = CreateLuaComponents(); components.Register<LuaPosition>("probe:position");
            builder.RegisterInstance(Bindings = new LuaBindingsTestScope(components)).AsSelf();
            builder.Register(_ => new ModsLifecycleManager(new LuaScriptLoader(new Karpik.Engine.Core.FileSystem.PhysicalFileSystem(), logger)))
                .As<IModsLifecycleManager>().SingleInstance();
            builder.Register(_ => CreateNative()).AsSelf().SingleInstance();
            builder.Register(context =>
            {
                RuntimeRegistry = new RecordingRuntimeRegistry(context.Resolve<ISystemRuntimeRegistry>());
                return context.Resolve<LuaBindingsTestScope>().Create(new FixedRegistry(definition),
                    context.Resolve<IModsLifecycleManager>(), context.Resolve<Application>(), RuntimeRegistry, context.Resolve<Time>(), logger);
            }).As<ISimulationPreparation>().SingleInstance();
        }
    }

    private sealed class StructuralRunnerModule : IModule
    {
        public void Add(ISystemRegistry systems) => systems.Add<StructuralNativeObserver>();
    }

    private sealed class StructuralRunnerComposition(StructuralRunnerInstaller installer) : IStaticRuntimeComposition
    {
        public void RegisterModules(IStaticModuleRegistry registry) => registry.Add(installer);
        public void RegisterServices(IStaticServiceRegistry registry) =>
            registry.Register<StructuralNativeObserver, StructuralNativeObserver>(ModuleScope.Simulation,
                ServiceLifetime.Singleton, _ => installer.CreateNative());
#pragma warning disable CS0436 // Use this test compilation's registry, which contains its fixture systems.
        public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry) =>
            registry.AddUpdate(new Karpik.Engine.Shared.ECS.Scheduling.Generated.GeneratedEcsUpdateRegistryProvider());
#pragma warning restore CS0436
    }

    [SequentialSystem]
    internal sealed class StructuralNativeObserver(LuaBindingsTestScope bindings, ScriptLogger logger) : ISystemUpdate
    {
        public int ObservedCount { get; private set; }
        public void Update()
        {
            var positions = bindings.World.GetPool<LuaPosition>();
            ObservedCount = positions.Count;
            foreach (int entity in positions.ToSpan())
            {
                ref var position = ref positions.Get(entity);
                // Newly spawned data must be initialized before native execution in this frame.
                Assert.True(position.X is 10 or 110);
                if (position.X == 10) position.X += 100;
            }
            logger.Messages.Add("native:" + ObservedCount);
        }
    }
}
