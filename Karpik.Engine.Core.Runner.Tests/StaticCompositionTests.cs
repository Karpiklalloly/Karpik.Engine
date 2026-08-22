using System.Composition;
using Autofac;
using Karpik.Engine.Core;
using Xunit;
using Xunit.Abstractions;

public sealed class StaticCompositionTests
{
    private readonly ITestOutputHelper _output;

    public StaticCompositionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Setup_StaticComposition_RegistersModuleAndResolvesServicesInAllScopes()
    {
        StaticTrace.Clear();
        EngineRunner runner = SetupStaticRunner();

        Assert.True(StaticTrace.SystemInitialized);
        Assert.True(StaticTrace.EngineServiceResolvedFromSimulation);
        Assert.True(StaticTrace.ModSetServiceResolvedFromSimulation);

        runner.Destroy();
    }

    [Fact]
    public void Setup_StaticSingletonSharesInstanceAcrossExportContracts()
    {
        StaticTrace.Clear();
        EngineRunner runner = SetupStaticRunner();

        Assert.True(StaticTrace.SingletonSharedAcrossContracts);

        runner.Destroy();
    }

    [Fact]
    public void Setup_StaticTransientRecreatesInstancePerResolution()
    {
        StaticTrace.Clear();
        EngineRunner runner = SetupStaticRunner();

        Assert.True(StaticTrace.TransientRecreated);

        runner.Destroy();
    }

    [Fact]
    public void Setup_StaticStartsIStartableExactlyOnce()
    {
        StaticTrace.Clear();
        EngineRunner runner = SetupStaticRunner();

        Assert.Equal(1, StaticTrace.StartCount);

        runner.Destroy();
    }

    [Fact]
    public void Destroy_StaticRunner_DisposesWithoutErrors()
    {
        StaticTrace.Clear();
        EngineRunner runner = SetupStaticRunner();

        runner.Destroy();
        runner.Destroy();

        Assert.True(StaticTrace.SystemInitialized);
    }

    [Fact]
    public void Setup_Static_DoesNotInvokeAttributedServiceRegistrarOrTypeActivation()
    {
        StaticTrace.Clear();
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        var runner = new EngineRunner();
        var registrarCalls = new List<ModuleScope>();
        runner.AttributedServiceRegistrationProbe += (builder, types, scope) => registrarCalls.Add(scope);

        runner.RegisterStaticComposition(new ProbeStaticComposition());
        runner.Setup(new Application(Side.Server), scheduler);
        scheduler.Execute();
        runner.Destroy();

        // Structural reflection guard (resolved decision 4): the Dynamic-mode
        // attributed registration path must never be entered during Static setup.
        Assert.Empty(registrarCalls);
        Assert.True(StaticTrace.SystemInitialized);
    }

    [Fact]
    public void RegisterModules_ViaEngineRunner_StillAcceptsDirectInstallerBeforeSetup()
    {
        StaticTrace.Clear();
        var runner = new EngineRunner();
        runner.RegisterStaticComposition(new ModuleOnlyComposition());

        IModuleInstaller installer = Assert.Single(runner.GetModules());

        Assert.IsType<StaticProbeModuleInstaller>(installer);
    }

    [Fact]
    public void StartupAllocations_DynamicVersusStatic_AreMeasuredAndDocumented()
    {
        // Warm-up both paths so JIT and static state do not skew the measurement.
        MeasureSetupBytes(dynamicMode: true);
        MeasureSetupBytes(dynamicMode: false);

        long dynamicBytes = MeasureSetupBytes(dynamicMode: true);
        long staticBytes = MeasureSetupBytes(dynamicMode: false);

        _output.WriteLine($"Dynamic startup allocations: {dynamicBytes} bytes");
        _output.WriteLine($"Static  startup allocations: {staticBytes} bytes");

        Assert.True(dynamicBytes > 0);
        Assert.True(staticBytes > 0);
    }

    private long MeasureSetupBytes(bool dynamicMode)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        EngineRunner runner = dynamicMode ? SetupDynamicRunner() : SetupStaticRunner();
        runner.Destroy();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static EngineRunner SetupDynamicRunner()
    {
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        var runner = new EngineRunner();
        runner.RegisterTypes([typeof(DynamicOnlyMarkerInstaller)]);
        runner.Setup(new Application(Side.Server), scheduler);
        scheduler.Execute();
        return runner;
    }

    private static EngineRunner SetupStaticRunner()
    {
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        var runner = new EngineRunner();
        runner.RegisterStaticComposition(new ProbeStaticComposition());
        runner.Setup(new Application(Side.Server), scheduler);
        scheduler.Execute();
        return runner;
    }
}

internal static class StaticTrace
{
    public static bool SystemConstructed { get; set; }
    public static bool SystemInitialized { get; set; }
    public static bool SingletonSharedAcrossContracts { get; set; }
    public static bool TransientRecreated { get; set; }
    public static bool EngineServiceResolvedFromSimulation { get; set; }
    public static bool ModSetServiceResolvedFromSimulation { get; set; }
    public static int StartCount { get; set; }

    public static void Clear()
    {
        SystemConstructed = false;
        SystemInitialized = false;
        SingletonSharedAcrossContracts = false;
        TransientRecreated = false;
        EngineServiceResolvedFromSimulation = false;
        ModSetServiceResolvedFromSimulation = false;
        StartCount = 0;
    }
}

[Module(ModuleScope.Simulation)]
internal sealed class StaticProbeModuleInstaller : IModuleInstaller
{
    public string Name => nameof(StaticProbeModuleInstaller);

    public IModule CreateModule() => new StaticProbeModule();
}

internal sealed class StaticProbeModule : IModule
{
    public void Add(ISystemRegistry systems) => systems.Add<ProbeSystem>();
}

internal sealed class ProbeSystem : ISystemInit
{
    public ProbeSystem(
        IEngineSvc engineSvc,
        IModSetSvc modSetSvc,
        ISimSingleton singletonContract,
        SimSingleton concreteContract,
        ISimTransient transientFactory,
        IServiceResolver services)
    {
        StaticTrace.SystemConstructed = true;
        StaticTrace.EngineServiceResolvedFromSimulation = engineSvc is EngineSvc;
        StaticTrace.ModSetServiceResolvedFromSimulation = modSetSvc is ModSetSvc;
        StaticTrace.SingletonSharedAcrossContracts =
            ReferenceEquals(singletonContract, concreteContract);
        StaticTrace.TransientRecreated = !ReferenceEquals(
            transientFactory,
            services.Resolve<ISimTransient>());
    }

    public void Init() => StaticTrace.SystemInitialized = true;
}

internal interface IEngineSvc;

[Export(typeof(IEngineSvc))]
[ServiceRegistration(ModuleScope.Engine)]
internal sealed class EngineSvc : IEngineSvc;

internal interface IModSetSvc;

[Export(typeof(IModSetSvc))]
[ServiceRegistration(ModuleScope.ModSet)]
internal sealed class ModSetSvc : IModSetSvc;

public interface ISimSingleton;

[Export(typeof(ISimSingleton))]
[Export(typeof(SimSingleton))]
[ServiceRegistration(ModuleScope.Simulation)]
internal sealed class SimSingleton : ISimSingleton;

public interface ISimTransient;

[Export(typeof(ISimTransient))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Transient)]
internal sealed class SimTransient : ISimTransient;

public interface IStartableSvc;

[Export(typeof(IStartableSvc))]
[ServiceRegistration(ModuleScope.Simulation)]
internal sealed class StartableSvc : IStartableSvc, IStartable
{
    public void Start() => StaticTrace.StartCount++;
}

internal sealed class ProbeStaticComposition : IStaticRuntimeComposition
{
    public void RegisterModules(IStaticModuleRegistry registry) =>
        registry.Add(new StaticProbeModuleInstaller());

    public void RegisterServices(IStaticServiceRegistry registry)
    {
        registry.Register<IEngineSvc, EngineSvc>(
            ModuleScope.Engine,
            ServiceLifetime.Singleton,
            static resolver => new EngineSvc());
        registry.Register<IModSetSvc, ModSetSvc>(
            ModuleScope.ModSet,
            ServiceLifetime.Singleton,
            static _ => new ModSetSvc());
        registry.Register<ISimSingleton, SimSingleton>(
            ModuleScope.Simulation,
            ServiceLifetime.Singleton,
            static _ => new SimSingleton());
        registry.Register<SimSingleton, SimSingleton>(
            ModuleScope.Simulation,
            ServiceLifetime.Singleton,
            static _ => new SimSingleton());
        registry.Register<ISimTransient, SimTransient>(
            ModuleScope.Simulation,
            ServiceLifetime.Transient,
            static _ => new SimTransient());
        registry.Register<IStartableSvc, StartableSvc>(
            ModuleScope.Simulation,
            ServiceLifetime.Singleton,
            static _ => new StartableSvc());
        registry.Register<ProbeSystem, ProbeSystem>(
            ModuleScope.Simulation,
            ServiceLifetime.Transient,
            static resolver => new ProbeSystem(
                resolver.Resolve<IEngineSvc>(),
                resolver.Resolve<IModSetSvc>(),
                resolver.Resolve<ISimSingleton>(),
                resolver.Resolve<SimSingleton>(),
                resolver.Resolve<ISimTransient>(),
                resolver.Resolve<IServiceResolver>()));
    }
}

internal sealed class ModuleOnlyComposition : IStaticRuntimeComposition
{
    public void RegisterModules(IStaticModuleRegistry registry) =>
        registry.Add(new StaticProbeModuleInstaller());

    public void RegisterServices(IStaticServiceRegistry registry) { }
}

[Module(ModuleScope.Simulation)]
internal sealed class DynamicOnlyMarkerInstaller : IModuleInstaller
{
    public string Name => nameof(DynamicOnlyMarkerInstaller);
}
