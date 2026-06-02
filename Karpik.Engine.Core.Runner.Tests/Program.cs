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

        runner.RegisterModule(new LifecycleSmokeInstaller());
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
        var runner = Setup(new ZetaInstaller(), new AlphaInstaller());

        AssertSequence(["Alpha.Register", "Zeta.Register"], OrderTrace.Items);
        runner.Destroy();
    }

    [Fact]
    public void EngineRunner_Setup_UsesPriorityBeforeTypeName()
    {
        OrderTrace.Clear();
        var runner = Setup(new AlphaLateInstaller(), new ZetaEarlyInstaller());

        AssertSequence(["ZetaEarly.Register", "AlphaLate.Register"], OrderTrace.Items);
        runner.Destroy();
    }

    [Fact]
    public void EngineRunner_Destroy_DestroysInstallersInReverseInitOrder()
    {
        OrderTrace.Clear();
        var runner = Setup(new ZetaInstaller(), new AlphaInstaller());
        OrderTrace.Clear();

        runner.Destroy();

        AssertSequence(["Zeta.Destroy", "Alpha.Destroy"], OrderTrace.Items);
    }

    private static EngineRunner Setup(params IInstaller[] installers)
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
sealed class AlphaInstaller : IInstallerDestroy
{
    public string Name => nameof(AlphaInstaller);
    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer) => OrderTrace.Add("Alpha.Register");
    public void Destroy() => OrderTrace.Add("Alpha.Destroy");
}

[Module]
sealed class ZetaInstaller : IInstallerDestroy
{
    public string Name => nameof(ZetaInstaller);
    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer) => OrderTrace.Add("Zeta.Register");
    public void Destroy() => OrderTrace.Add("Zeta.Destroy");
}

[Module(10)]
sealed class AlphaLateInstaller : IInstallerDestroy
{
    public string Name => nameof(AlphaLateInstaller);
    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer) => OrderTrace.Add("AlphaLate.Register");
    public void Destroy() => OrderTrace.Add("AlphaLate.Destroy");
}

[Module(-10)]
sealed class ZetaEarlyInstaller : IInstallerDestroy
{
    public string Name => nameof(ZetaEarlyInstaller);
    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer) => OrderTrace.Add("ZetaEarly.Register");
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

sealed class LifecycleSmokeInstaller : IInstallerConfiguratable
{
    public string Name => nameof(LifecycleSmokeInstaller);

    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer)
    {
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        module = new LifecycleSmokeModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
    }
}

sealed class LifecycleSmokeModule : IModule
{
    public void Import(IBuilder builder)
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
