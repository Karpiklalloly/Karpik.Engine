using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Client.InputModule;

[Module]
public class InputModuleInstaller : IModuleInstaller, IModuleInstallerConfiguratable
{
    public string Name => "Input";

    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(new Input());
    }

    public void OnConfigure(IServiceResolver services, out IModule? module)
    {
        services.Get<Input>()!.Init(services.Get<IInputSource>()!, services.Get<InputCaptureState>()!);
        module = new InputModuleEcs();
    }

    public void OnConfigureComplete(IServiceResolver services)
    {
        
    }
}
