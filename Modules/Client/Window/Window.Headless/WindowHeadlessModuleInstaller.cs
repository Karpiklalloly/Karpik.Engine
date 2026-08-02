using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;

namespace Karpik.Engine.Modules.Window.Headless;

[Module(ModuleScope.Engine, -200)]
public sealed class WindowHeadlessModuleInstaller : IModuleInstaller
{
    public string Name => "Window.Headless";

    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.RegisterType<HeadlessInputController>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HeadlessInputSource>()
            .As<IInputSource>()
            .SingleInstance();

        builder.RegisterType<HeadlessWindow>()
            .As<IWindow>()
            .AsSelf()
            .SingleInstance();
    }
}
