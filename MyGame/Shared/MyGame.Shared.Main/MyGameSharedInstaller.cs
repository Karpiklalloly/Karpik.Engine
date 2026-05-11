using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.MyGame.Shared.Main;

[Module]
public class MyGameSharedInstaller : IInstaller
{
    public string Name => "MyGame.Shared.Main";
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(new NetworkManager());
    }
}