using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.ECS;

[Module]
public class ECSInstaller : IModule
{
    public string Name => "ECS.Core";
    
    private EcsDefaultWorld _defaultWorld = new();
    private EcsEventWorld _eventWorld = new();
    private EcsMetaWorld _metaWorld = new();
    
    public void OnRegisterServices(IServiceRegister services)
    {
        services
            .Register(_defaultWorld)
            .Register(_eventWorld)
            .Register(_metaWorld);
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