using System.Composition;
using Karpik.Engine.Core;
using Xunit;

public sealed class AutofacCompositionTests
{
    [Fact]
    public void Setup_ResolvesSystemsFromSimulationScopeWithEngineDependencies()
    {
        CompositionTrace.Clear();
        EngineRunner runner = SetupRunner();

        Assert.True(CompositionTrace.SystemWasConstructed);
        Assert.True(CompositionTrace.SingletonWasReused);
        Assert.True(CompositionTrace.TransientWasRecreated);
        Assert.Equal(2, CompositionTrace.CollectionCount);

        runner.Destroy();
    }

    [Fact]
    public void Setup_RestoresRestartWorkerStateBeforeSystemInitialization()
    {
        CompositionTrace.Clear();
        EngineRunner runner = SetupRunner(new Dictionary<string, byte[]>
        {
            [RestartStateProbe.StateKey] = [42]
        });

        Assert.Equal(["Restore:42", "Init"], CompositionTrace.RestartEvents);

        Dictionary<string, byte[]> state = runner.GetHotReloadData();
        Assert.Equal([84], state[RestartStateProbe.StateKey]);
        runner.Destroy();
    }

    [Fact]
    public void Destroy_DisposesOwnedSyncAndAsyncServicesExactlyOnce()
    {
        CompositionTrace.Clear();
        EngineRunner runner = SetupRunner();

        runner.Destroy();
        runner.Destroy();

        Assert.Equal(1, CompositionTrace.SyncDisposeCount);
        Assert.Equal(1, CompositionTrace.AsyncDisposeCount);
    }

    private static EngineRunner SetupRunner(Dictionary<string, byte[]>? hotReloadData = null)
    {
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        var runner = new EngineRunner();
        runner.RegisterTypes(
        [
            typeof(CompositionModuleInstaller),
            typeof(EngineProbe),
            typeof(ModSetProbe),
            typeof(CollectionProbeA),
            typeof(CollectionProbeB),
            typeof(SimulationSingletonProbe),
            typeof(SimulationTransientProbe),
            typeof(SyncDisposableProbe),
            typeof(AsyncDisposableProbe),
            typeof(RestartStateProbe)
        ]);
        runner.Setup(new Application(Side.Server), scheduler, hotReloadData);
        scheduler.Execute();
        return runner;
    }
}

internal static class CompositionTrace
{
    public static bool SystemWasConstructed { get; set; }
    public static bool SingletonWasReused { get; set; }
    public static bool TransientWasRecreated { get; set; }
    public static int SyncDisposeCount { get; set; }
    public static int AsyncDisposeCount { get; set; }
    public static int CollectionCount { get; set; }
    public static List<string> RestartEvents { get; } = [];

    public static void Clear()
    {
        SystemWasConstructed = false;
        SingletonWasReused = false;
        TransientWasRecreated = false;
        SyncDisposeCount = 0;
        AsyncDisposeCount = 0;
        CollectionCount = 0;
        RestartEvents.Clear();
    }
}

[Module(ModuleScope.Simulation)]
internal sealed class CompositionModuleInstaller : IModuleInstaller
{
    public string Name => nameof(CompositionModuleInstaller);
    public IModule CreateModule() => new CompositionModule();
}

internal sealed class CompositionModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<CompositionSystem>();
    }
}

internal sealed class CompositionSystem : ISystemInit
{
    private readonly IServiceResolver _services;
    private readonly RestartStateProbe _restartState;

    public CompositionSystem(
        EngineProbe engineProbe,
        ModSetProbe modSetProbe,
        IServiceResolver services,
        RestartStateProbe restartState,
        ICollectionProbe[] collectionProbes,
        SyncDisposableProbe syncDisposable,
        AsyncDisposableProbe asyncDisposable)
    {
        _ = engineProbe;
        _ = modSetProbe;
        _services = services;
        _restartState = restartState;
        CompositionTrace.CollectionCount = collectionProbes.Length;
        _ = syncDisposable;
        _ = asyncDisposable;
        CompositionTrace.SystemWasConstructed = true;
    }

    public void Init()
    {
        CompositionTrace.SingletonWasReused = ReferenceEquals(
            _services.Resolve<SimulationSingletonProbe>(),
            _services.Resolve<SimulationSingletonProbe>());
        CompositionTrace.TransientWasRecreated = !ReferenceEquals(
            _services.Resolve<SimulationTransientProbe>(),
            _services.Resolve<SimulationTransientProbe>());
        _ = _restartState;
        CompositionTrace.RestartEvents.Add("Init");
    }
}

[Export(typeof(EngineProbe))]
[ServiceRegistration(ModuleScope.Engine)]
internal sealed class EngineProbe;

[Export(typeof(ModSetProbe))]
[ServiceRegistration(ModuleScope.ModSet)]
internal sealed class ModSetProbe;

internal interface ICollectionProbe;

[Export(typeof(ICollectionProbe))]
[ServiceRegistration(ModuleScope.Engine)]
internal sealed class CollectionProbeA : ICollectionProbe;

[Export(typeof(ICollectionProbe))]
[ServiceRegistration(ModuleScope.Engine)]
internal sealed class CollectionProbeB : ICollectionProbe;

[Export(typeof(SimulationSingletonProbe))]
[ServiceRegistration(ModuleScope.Simulation)]
internal sealed class SimulationSingletonProbe;

[Export(typeof(SimulationTransientProbe))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Transient)]
internal sealed class SimulationTransientProbe;

[Export(typeof(SyncDisposableProbe))]
[ServiceRegistration(ModuleScope.Simulation)]
internal sealed class SyncDisposableProbe : IDisposable
{
    public void Dispose() => CompositionTrace.SyncDisposeCount++;
}

[Export(typeof(AsyncDisposableProbe))]
[ServiceRegistration(ModuleScope.Simulation)]
internal sealed class AsyncDisposableProbe : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        CompositionTrace.AsyncDisposeCount++;
        return ValueTask.CompletedTask;
    }
}

[Export(typeof(IRestartWorkerStateProvider))]
[Export(typeof(RestartStateProbe))]
[ServiceRegistration(ModuleScope.Simulation)]
internal sealed class RestartStateProbe : IRestartWorkerStateProvider
{
    public const string StateKey = "Runner.Tests.RestartState";
    public string Key => StateKey;

    public byte[] Capture() => [84];

    public void Restore(ReadOnlySpan<byte> data)
    {
        CompositionTrace.RestartEvents.Add($"Restore:{data[0]}");
    }
}
