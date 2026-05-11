using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Network.Server.LiteNetLib;

[Module]
public class NetworkServerInstaller : IInstaller, IInstallerConfiguratable
{
    public string Name => "Network.Server.LiteNetLib";
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        module = new NetworkServerModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}