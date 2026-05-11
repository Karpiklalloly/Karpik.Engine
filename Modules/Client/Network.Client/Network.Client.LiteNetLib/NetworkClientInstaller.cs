using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Network.LiteNetLib;

[Module]
public class NetworkClientInstaller : IInstaller, IInstallerConfiguratable
{
    public string Name => "Network.Client.Core";
    
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        module = new NetworkClientModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}