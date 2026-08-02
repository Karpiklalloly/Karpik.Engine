using Autofac;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

[Module(ModuleScope.Simulation)]
public class InputModuleInstaller : IModuleInstaller
{
    public string Name => "Input";

    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.RegisterType<Input>()
            .AsSelf()
            .SingleInstance();
    }

    public IModule? CreateModule() => new InputModuleEcs();
}
