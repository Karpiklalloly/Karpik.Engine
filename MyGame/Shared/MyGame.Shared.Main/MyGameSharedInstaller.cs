using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.MyGame.Shared.Main;

[Module]
public class MyGameSharedInstaller : IModule, IModuleConfiguratable
{
    public string Name => "MyGame.Shared.Main";
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(new NetworkManager());
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}