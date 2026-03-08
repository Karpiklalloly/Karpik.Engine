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

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}