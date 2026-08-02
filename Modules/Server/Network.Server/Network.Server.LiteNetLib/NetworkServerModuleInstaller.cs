using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Network.Server.LiteNetLib;

[Module]
public class NetworkServerModuleInstaller : IModuleInstaller, IModuleInstallerConfiguratable
{
    public string Name => "Network.Server.LiteNetLib";
    public void OnRegisterServices(ContainerBuilder builder)
    {
        
    }

    public void OnConfigure(IServiceResolver services, out IModule? module)
    {
        module = new NetworkServerModule();
    }

    public void OnConfigureComplete(IServiceResolver services)
    {
        
    }
}