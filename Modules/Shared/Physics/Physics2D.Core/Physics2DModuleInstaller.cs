using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Shared.Physics.Core;

[Module]
public class Physics2DModuleInstaller : IModuleInstaller, IModuleInstallerConfiguratable
{
    public string Name => "Physics2D.Core";
    public void OnRegisterServices(ContainerBuilder builder)
    {
        
    }

    public void OnConfigure(IServiceResolver services, out IModule? module)
    {
        module = new Physics2DModule();
    }

    public void OnConfigureComplete(IServiceResolver services)
    {
        
    }
}