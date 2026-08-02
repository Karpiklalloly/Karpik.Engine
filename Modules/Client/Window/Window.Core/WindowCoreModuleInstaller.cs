using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Modules.Window.Core;

[Module]
public class WindowCoreModuleInstaller : IModuleInstaller, IModuleInstallerConfiguratable
{
    public string Name => "Window.Core";
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(new InputCaptureState());
    }

    public void OnConfigure(IServiceResolver services, out IModule? module)
    {
        module = new WindowCoreModule();
    }

    public void OnConfigureComplete(IServiceResolver services)
    {
        
    }
}
