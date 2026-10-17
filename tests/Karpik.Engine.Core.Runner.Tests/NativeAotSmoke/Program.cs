using System.Composition;
using Autofac;
using Karpik.Engine.Core;

// Milestone 5 AOT smoke (resolved decision #5): a hand-written composition drives a
// real EngineRunner setup/destroy through the Static registration boundary. Running
// the GENERATED composition under NativeAOT belongs to Milestone 8.
var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
var runner = new EngineRunner();
runner.RegisterStaticComposition(new SmokeComposition());
runner.Setup(new Application(Side.Server), scheduler);
scheduler.Execute();

if (!SmokeTrace.SystemInitialized || !SmokeTrace.ServiceResolved)
{
    throw new InvalidOperationException(
        $"Static smoke startup failed: initialized={SmokeTrace.SystemInitialized}, serviceResolved={SmokeTrace.ServiceResolved}");
}

runner.Destroy();
Console.WriteLine("STATIC_AOT_OK");

internal static class SmokeTrace
{
    public static bool SystemInitialized { get; set; }
    public static bool ServiceResolved { get; set; }
}

[Module(ModuleScope.Simulation)]
internal sealed class SmokeModuleInstaller : IModuleInstaller
{
    public string Name => nameof(SmokeModuleInstaller);

    public IModule CreateModule() => new SmokeModule();
}

internal sealed class SmokeModule : IModule
{
    public void Add(ISystemRegistry systems) => systems.Add<SmokeSystem>();
}

public interface ISmokeService;

[Export(typeof(ISmokeService))]
[ServiceRegistration(ModuleScope.Simulation)]
internal sealed class SmokeService : ISmokeService;

internal sealed class SmokeSystem(ISmokeService service) : ISystemInit
{
    public void Init()
    {
        SmokeTrace.ServiceResolved = service is SmokeService;
        SmokeTrace.SystemInitialized = true;
    }
}

internal sealed class SmokeComposition : IStaticRuntimeComposition
{
    public void RegisterModules(IStaticModuleRegistry registry) =>
        registry.Add(new SmokeModuleInstaller());

    public void RegisterServices(IStaticServiceRegistry registry)
    {
        registry.Register<ISmokeService, SmokeService>(
            ModuleScope.Simulation,
            ServiceLifetime.Singleton,
            static _ => new SmokeService());
        registry.Register<SmokeSystem, SmokeSystem>(
            ModuleScope.Simulation,
            ServiceLifetime.Transient,
            static resolver => new SmokeSystem(resolver.Resolve<ISmokeService>()));
    }

    public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry) { }
}
