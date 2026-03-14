using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.LiteNetLib.Configs;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

[Module]
public class NetworkInstaller : IModule, IModuleConfiguratable
{
    public string Name => "Network.Shared.Core";
 
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(new NetworkConfig());
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}