using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Modules.Window.Core;
using Karpik.Engine.Modules.Window.Headless;
using Karpik.Engine.Shared.ECS.Scheduling;
using Veldrid;
using Xunit;

namespace Karpik.Engine.Client.InputModule.Tests;

public sealed class HeadlessWindowInputIntegrationTests
{
    [Fact]
    public void MainThreadBeginThenStepBegin_PublishesHeadlessKeyboardEdgesThroughInputService()
    {
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        var runner = new EngineRunner
        {
            UpdateSchedulerMode = EcsUpdateSchedulerMode.Deterministic
        };
        Type[] moduleTypes = typeof(WindowCoreModuleInstaller).Assembly.GetTypes()
            .Concat(typeof(WindowHeadlessModuleInstaller).Assembly.GetTypes())
            .Concat(typeof(InputModuleInstaller).Assembly.GetTypes())
            .Distinct()
            .ToArray();
        runner.RegisterTypes(moduleTypes);
        runner.RegisterModule(new CaptureInputInstaller());
        runner.Setup(new Application(Side.Client), scheduler);
        scheduler.Execute();

        GameplayLoopDriver driver = runner.CreateGameplayLoopDriver();

        CaptureInputSystem.Controller!.PressKey(Key.A);
        runner.RunMainThreadBegin();
        driver.StepBegin();

        Assert.True(CaptureInputSystem.Input!.IsPressed(Key.A));
        Assert.True(CaptureInputSystem.Input.IsDown(Key.A));

        runner.RunMainThreadBegin();
        driver.StepBegin();

        Assert.False(CaptureInputSystem.Input.IsPressed(Key.A));
        Assert.True(CaptureInputSystem.Input.IsDown(Key.A));

        CaptureInputSystem.Controller.ReleaseKey(Key.A);
        runner.RunMainThreadBegin();
        driver.StepBegin();

        Assert.True(CaptureInputSystem.Input.IsUnPressed(Key.A));
        Assert.True(CaptureInputSystem.Input.IsUp(Key.A));

        runner.Destroy();
        CaptureInputSystem.Clear();
    }

    [Module(ModuleScope.Simulation)]
    private sealed class CaptureInputInstaller : IModuleInstaller
    {
        public string Name => nameof(CaptureInputInstaller);

        public IModule CreateModule() => new CaptureInputModule();
    }

    private sealed class CaptureInputModule : IModule
    {
        public void Add(ISystemRegistry systems)
        {
            systems.Add<CaptureInputSystem>(CustomLayers.BEGIN_PROGRAM_LAYER, -900);
        }
    }

    private sealed class CaptureInputSystem : ISystemBegin
    {
        private readonly Input _input;

        public static Input? Input { get; private set; }
        public static HeadlessInputController? Controller { get; private set; }

        public CaptureInputSystem(Input input, HeadlessInputController controller)
        {
            _input = input;
            Controller = controller;
        }

        public void Begin()
        {
            Input = _input;
        }

        public static void Clear()
        {
            Input = null;
            Controller = null;
        }
    }
}
