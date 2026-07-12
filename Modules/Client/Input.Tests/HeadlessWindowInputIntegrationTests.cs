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
        var headlessWindow = new WindowHeadlessInstaller();

        runner.RegisterModule(new WindowCoreInstaller());
        runner.RegisterModule(headlessWindow);
        runner.RegisterModule(new InputInstaller());
        runner.RegisterModule(new CaptureInputInstaller());
        runner.Setup(new Application(Side.Client), scheduler);
        scheduler.Execute();

        GameplayLoopDriver driver = runner.CreateGameplayLoopDriver();

        headlessWindow.Controller.PressKey(Key.A);
        runner.RunMainThreadBegin();
        driver.StepBegin();

        Assert.True(CaptureInputSystem.Input!.IsPressed(Key.A));
        Assert.True(CaptureInputSystem.Input.IsDown(Key.A));

        runner.RunMainThreadBegin();
        driver.StepBegin();

        Assert.False(CaptureInputSystem.Input.IsPressed(Key.A));
        Assert.True(CaptureInputSystem.Input.IsDown(Key.A));

        headlessWindow.Controller.ReleaseKey(Key.A);
        runner.RunMainThreadBegin();
        driver.StepBegin();

        Assert.True(CaptureInputSystem.Input.IsUnPressed(Key.A));
        Assert.True(CaptureInputSystem.Input.IsUp(Key.A));

        runner.Destroy();
        CaptureInputSystem.Clear();
    }

    private sealed class CaptureInputInstaller : IInstallerConfiguratable
    {
        public string Name => nameof(CaptureInputInstaller);

        public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer)
        {
        }

        public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
        {
            module = new CaptureInputModule();
        }

        public void OnConfigureComplete(IServiceContainer services)
        {
        }
    }

    private sealed class CaptureInputModule : IModule
    {
        public void Import(IBuilder builder)
        {
            builder.Add(new CaptureInputSystem(), CustomLayers.BEGIN_PROGRAM_LAYER, -900);
        }
    }

    private sealed class CaptureInputSystem : ISystemBegin
    {
        [DI] private Input _input = null!;

        public static Input? Input { get; private set; }

        public void Begin()
        {
            Input = _input;
        }

        public static void Clear()
        {
            Input = null;
        }
    }
}
