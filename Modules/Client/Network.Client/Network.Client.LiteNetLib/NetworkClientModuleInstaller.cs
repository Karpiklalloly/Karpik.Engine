using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Client.Network.LiteNetLib;

[Module]
public class NetworkClientModuleInstaller : IModuleInstaller, IModuleInstallerConfiguratable
{
    public string Name => "Network.Client.Core";
    
    public void OnRegisterServices(ContainerBuilder builder)
    {
        
    }

    public void OnConfigure(IServiceResolver services, out IModule? module)
    {
        module = new NetworkClientModule();
    }

    public void OnConfigureComplete(IServiceResolver services)
    {
        
    }
}