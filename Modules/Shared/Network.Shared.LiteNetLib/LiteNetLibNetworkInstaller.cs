using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

[Module]
public class LiteNetLibNetworkInstaller : IInstaller
{
    public string Name => "Network.Shared.LiteNetLib";
 
    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer)
    {
        services.Register<INetworkManager>(new LiteNetLibNetworkManager());
    }
}