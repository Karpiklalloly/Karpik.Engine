using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Network.Core;

[Module]
public class NetworkClientInstaller : IModule
{
    public string Name => "Network.Client.Core";
    
    public void OnRegisterServices(IServiceRegister services)
    {
        
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = new NetworkClientModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}