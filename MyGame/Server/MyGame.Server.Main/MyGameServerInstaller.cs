using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Log;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Server.Main;

[Module]
public class MyGameServerInstaller : IModule
{
    public string Name => "MyGame.Server.Main";
    
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register<ITargetRpcSender>(new TargetRpcSender());
        services.Register(new CommandDispatcher());
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = new MyGameServerModule();
    }
    
    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}