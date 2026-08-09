using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Xunit;

public sealed class EngineRunnerLifecycleTests
{
    [Fact]
    public void EngineRunner_Run_ExecutesLifecyclePhasesIn04Order()
    {
        LifecycleTrace.Clear();

        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        var runner = new EngineRunner();

        runner.RegisterModule(new LifecycleSmokeModuleInstaller());
        runner.Setup(new Application(Side.Server), scheduler);
        scheduler.Execute();

        runner.Run(Application.TICK_DT);
        runner.Destroy();

        AssertSequence(
            ["Init", "Begin", "FixedUpdate", "Update", "LateUpdate", "Render", "Destroy"],
            LifecycleTrace.Items);
    }

    [Fact]
    public void EngineRunner_Setup_SortsEqualPriorityInstallersByTypeName()
    {
        OrderTrace.Clear();
        var runner = Setup(new ZetaModuleInstaller(), new AlphaModuleInstaller());

        AssertSequence(["Alpha.Register", "Zeta.Register"], OrderTrace.Items);
        runner.Destroy();
    }

    [Fact]
    public void EngineRunner_Setup_UsesPriorityBeforeTypeName()
    {
        OrderTrace.Clear();
        var runner = Setup(new AlphaLateModuleInstaller(), new ZetaEarlyModuleInstaller());

        AssertSequence(["ZetaEarly.Register", "AlphaLate.Register"], OrderTrace.Items);
        runner.Destroy();
    }

    private static EngineRunner Setup(params IModuleInstaller[] installers)
    {
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        var runner = new EngineRunner();
        foreach (var installer in installers)
        {
            runner.RegisterModule(installer);
        }
        runner.Setup(new Application(Side.Server), scheduler);
        scheduler.Execute();
        return runner;
    }

    private static void AssertSequence(string[] expected, IReadOnlyList<string> actual)
    {
        if (expected.Length != actual.Count)
        {
            throw new InvalidOperationException(
                $"Expected {expected.Length} lifecycle events, got {actual.Count}: {string.Join(", ", actual)}");
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Lifecycle event {i} mismatch. Expected '{expected[i]}', got '{actual[i]}'. Full order: {string.Join(", ", actual)}");
            }
        }
    }
}

static class OrderTrace
{
    private static readonly List<string> _items = [];
    public static IReadOnlyList<string> Items => _items;
    public static void Clear() => _items.Clear();
    public static void Add(string phase) => _items.Add(phase);
}

[Module(ModuleScope.Simulation)]
sealed class AlphaModuleInstaller : IModuleInstaller
{
    public string Name => nameof(AlphaModuleInstaller);
    public void OnRegisterServices(ContainerBuilder builder) => OrderTrace.Add("Alpha.Register");
}

[Module(ModuleScope.Simulation)]
sealed class ZetaModuleInstaller : IModuleInstaller
{
    public string Name => nameof(ZetaModuleInstaller);
    public void OnRegisterServices(ContainerBuilder builder) => OrderTrace.Add("Zeta.Register");
}

[Module(ModuleScope.Simulation, 10)]
sealed class AlphaLateModuleInstaller : IModuleInstaller
{
    public string Name => nameof(AlphaLateModuleInstaller);
    public void OnRegisterServices(ContainerBuilder builder) => OrderTrace.Add("AlphaLate.Register");
}

[Module(ModuleScope.Simulation, -10)]
sealed class ZetaEarlyModuleInstaller : IModuleInstaller
{
    public string Name => nameof(ZetaEarlyModuleInstaller);
    public void OnRegisterServices(ContainerBuilder builder) => OrderTrace.Add("ZetaEarly.Register");
}

static class LifecycleTrace
{
    private static readonly List<string> _items = [];

    public static IReadOnlyList<string> Items => _items;

    public static void Clear()
    {
        _items.Clear();
    }

    public static void Add(string phase)
    {
        _items.Add(phase);
    }
}

[Module(ModuleScope.Simulation)]
sealed class LifecycleSmokeModuleInstaller : IModuleInstaller
{
    public string Name => nameof(LifecycleSmokeModuleInstaller);

    public void OnRegisterServices(ContainerBuilder builder)
    {
    }

    public IModule CreateModule() => new LifecycleSmokeModule();
}

sealed class LifecycleSmokeModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<LifecycleSmokeSystem>();
    }
}

sealed class LifecycleSmokeSystem :
    ISystemInit,
    ISystemBegin,
    ISystemFixedUpdate,
    ISystemUpdate,
    ISystemLateUpdate,
    ISystemRender,
    ISystemDestroy
{
    public void Init() => LifecycleTrace.Add("Init");
    public void Begin() => LifecycleTrace.Add("Begin");
    public void FixedUpdate() => LifecycleTrace.Add("FixedUpdate");
    public void Update() => LifecycleTrace.Add("Update");
    public void LateUpdate() => LifecycleTrace.Add("LateUpdate");
    public void Render() => LifecycleTrace.Add("Render");
    public void Destroy() => LifecycleTrace.Add("Destroy");
}
