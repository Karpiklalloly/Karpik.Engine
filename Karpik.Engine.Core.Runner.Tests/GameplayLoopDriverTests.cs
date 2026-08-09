using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS.Scheduling;
using Xunit;

public sealed class GameplayLoopDriverTests
{
    [Fact]
    public void StepFixed_RunsFixedUpdateExactRequestedCount()
    {
        ManualGameplayTrace.Clear();
        EngineRunner runner = SetupRunner();
        GameplayLoopDriver driver = runner.CreateGameplayLoopDriver();

        driver.StepFixed(3);
        runner.Destroy();

        Assert.Equal(["Fixed", "Fixed", "Fixed", "Destroy"], ManualGameplayTrace.Items);
    }

    [Fact]
    public void StepUpdate_RunsScheduledUpdateWithoutOtherLifecyclePhases()
    {
        ManualGameplayTrace.Clear();
        EngineRunner runner = SetupRunner();
        GameplayLoopDriver driver = runner.CreateGameplayLoopDriver();

        driver.StepUpdate(Application.TICK_DT);
        runner.Destroy();

        Assert.Equal(["Update", "Destroy"], ManualGameplayTrace.Items);
    }

    [Fact]
    public void StepBegin_RunsBeginWithoutRenderOrRenderPrepare()
    {
        ManualGameplayTrace.Clear();
        EngineRunner runner = SetupRunner();
        GameplayLoopDriver driver = runner.CreateGameplayLoopDriver();

        driver.StepBegin();
        runner.Destroy();

        Assert.Equal(["Begin", "Destroy"], ManualGameplayTrace.Items);
    }

    [Fact]
    public void StepRun_RunsPipelineRunWithoutRenderOrRenderPrepare()
    {
        ManualGameplayTrace.Clear();
        EngineRunner runner = SetupRunner();
        GameplayLoopDriver driver = runner.CreateGameplayLoopDriver();

        driver.StepRun();
        runner.Destroy();

        Assert.Equal(["Destroy"], ManualGameplayTrace.Items);
    }

    [Fact]
    public void StepFrame_RunsAllNonRenderCyclesInRunnerOrder()
    {
        ManualGameplayTrace.Clear();
        EngineRunner runner = SetupRunner();
        GameplayLoopDriver driver = runner.CreateGameplayLoopDriver();

        driver.StepFrame(Application.TICK_DT, fixedTicks: 2);
        runner.Destroy();

        Assert.Equal(["Begin", "Fixed", "Fixed", "Update", "Late", "Destroy"], ManualGameplayTrace.Items);
    }

    [Fact]
    public void StepFixed_RejectsNegativeTickCount()
    {
        EngineRunner runner = SetupRunner();
        GameplayLoopDriver driver = runner.CreateGameplayLoopDriver();

        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => driver.StepFixed(-1)));
        runner.Destroy();
    }

    private static EngineRunner SetupRunner()
    {
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        var runner = new EngineRunner
        {
            UpdateSchedulerMode = EcsUpdateSchedulerMode.Deterministic
        };
        runner.RegisterModule(new ManualGameplayModuleInstaller());
        runner.Setup(new Application(Side.Server), scheduler);
        scheduler.Execute();
        ManualGameplayTrace.Clear();
        return runner;
    }
}

internal static class ManualGameplayTrace
{
    private static readonly List<string> _items = [];

    public static IReadOnlyList<string> Items => _items;

    public static void Clear()
    {
        _items.Clear();
    }

    public static void Add(string value)
    {
        _items.Add(value);
    }
}

[Module(ModuleScope.Simulation)]
internal sealed class ManualGameplayModuleInstaller : IModuleInstaller
{
    public string Name => nameof(ManualGameplayModuleInstaller);

    public IModule CreateModule() => new ManualGameplayModule();
}

internal sealed class ManualGameplayModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<ManualGameplaySystem>();
    }
}

internal sealed class ManualGameplaySystem :
    ISystemBegin,
    ISystemFixedUpdate,
    ISystemUpdate,
    ISystemLateUpdate,
    ISystemRenderPrepare,
    ISystemRender,
    ISystemDestroy
{
    public void Begin()
    {
        ManualGameplayTrace.Add("Begin");
    }

    public void FixedUpdate()
    {
        ManualGameplayTrace.Add("Fixed");
    }

    public void Update()
    {
        ManualGameplayTrace.Add("Update");
    }

    public void LateUpdate()
    {
        ManualGameplayTrace.Add("Late");
    }

    public void RenderPrepare()
    {
        ManualGameplayTrace.Add("RenderPrepare");
    }

    public void Render()
    {
        ManualGameplayTrace.Add("Render");
    }

    public void Destroy()
    {
        ManualGameplayTrace.Add("Destroy");
    }
}
