using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Network.Server.LiteNetLib;

[Module]
public class NetworkServerInstaller : IModule, IModuleConfiguratable
{
    public string Name => "Network.Server.LiteNetLib";
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = new NetworkServerModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}