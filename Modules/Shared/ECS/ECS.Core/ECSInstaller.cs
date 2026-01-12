using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.ECS;

[Module]
public class ECSInstaller : IModule
{
    public string Name => "ECS.Core";
    
    public void OnRegisterServices(IServiceRegister services)
    {
        services
            .Register(new EcsDefaultWorld())
            .Register(new EcsEventWorld())
            .Register(new EcsMetaWorld());
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = new ECSModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
    
    public void Initialize(IServiceContainer services)
    {
        
    }
}