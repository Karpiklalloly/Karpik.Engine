using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.LiteNetLib.Configs;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

[Module]
public class NetworkInstaller : IInstaller
{
    public string Name => "Network.Shared.Core";
 
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(new NetworkConfig());
    }
}