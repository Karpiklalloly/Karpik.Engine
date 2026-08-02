using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Microsoft.Extensions.DependencyInjection;
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
            ["Init", "Begin", "DragonRun", "FixedUpdate", "Update", "LateUpdate", "Render", "Destroy"],
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

    [Fact]
    public void EngineRunner_Destroy_DestroysInstallersInReverseInitOrder()
    {
        OrderTrace.Clear();
        var runner = Setup(new ZetaModuleInstaller(), new AlphaModuleInstaller());
        OrderTrace.Clear();

        runner.Destroy();

        AssertSequence(["Zeta.Destroy", "Alpha.Destroy"], OrderTrace.Items);
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

[Module]
sealed class AlphaModuleInstaller : IModuleInstallerDestroy
{
    public string Name => nameof(AlphaModuleInstaller);
    public void OnRegisterServices(ContainerBuilder builder) => OrderTrace.Add("Alpha.Register");
    public void Destroy() => OrderTrace.Add("Alpha.Destroy");
}

[Module]
sealed class ZetaModuleInstaller : IModuleInstallerDestroy
{
    public string Name => nameof(ZetaModuleInstaller);
    public void OnRegisterServices(ContainerBuilder builder) => OrderTrace.Add("Zeta.Register");
    public void Destroy() => OrderTrace.Add("Zeta.Destroy");
}

[Module(10)]
sealed class AlphaLateModuleInstaller : IModuleInstallerDestroy
{
    public string Name => nameof(AlphaLateModuleInstaller);
    public void OnRegisterServices(ContainerBuilder builder) => OrderTrace.Add("AlphaLate.Register");
    public void Destroy() => OrderTrace.Add("AlphaLate.Destroy");
}

[Module(-10)]
sealed class ZetaEarlyModuleInstaller : IModuleInstallerDestroy
{
    public string Name => nameof(ZetaEarlyModuleInstaller);
    public void OnRegisterServices(ContainerBuilder builder) => OrderTrace.Add("ZetaEarly.Register");
    public void Destroy() => OrderTrace.Add("ZetaEarly.Destroy");
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

sealed class LifecycleSmokeModuleInstaller : IModuleInstallerConfiguratable
{
    public string Name => nameof(LifecycleSmokeModuleInstaller);

    public void OnRegisterServices(ContainerBuilder builder)
    {
    }

    public void OnConfigure(IServiceResolver services, out IModule? module)
    {
        module = new LifecycleSmokeModule();
    }

    public void OnConfigureComplete(IServiceResolver services)
    {
    }
}

sealed class LifecycleSmokeModule : IModule
{
    public void Import(IBuilder builder, IServiceResolver services)
    {
        builder.Add((object)new LifecycleSmokeSystem());
        builder.Add(new LegacyDragonRunSystem());
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

sealed class LegacyDragonRunSystem : IEcsRun
{
    public void Run() => LifecycleTrace.Add("DragonRun");
}
