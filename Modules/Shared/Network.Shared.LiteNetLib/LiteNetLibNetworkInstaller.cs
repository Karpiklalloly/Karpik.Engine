using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

[Module]
public class LiteNetLibNetworkInstaller : IModule
{
    public string Name => "Network.Shared.LiteNetLib";
 
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register<INetworkManager>(new LiteNetLibNetworkManager());
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}