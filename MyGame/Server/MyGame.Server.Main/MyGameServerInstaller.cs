using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Log;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Server.Main;

[Module]
public class MyGameServerInstaller : IInstaller, IInstallerConfiguratable
{
    public string Name => "MyGame.Server.Main";
    
    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer)
    {
        services.Register<ITargetRpcSender>(new TargetRpcSender());
        services.Register(new CommandDispatcher());
        services.Register(new NetworkIdGenerator());
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        module = new MyGameServerModule();
    }
    
    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}