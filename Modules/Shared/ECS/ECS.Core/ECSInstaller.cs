using Karpik.Engine.Core;
using Karpik.Engine.Core.ModuleManagement;

namespace Karpik.Engine.Shared.ECS;

[Module]
public class ECSInstaller : IModule, IModuleHotReload
{
    public string Name => "ECS.Core";
    
    private EcsDefaultWorld _world = new();
    private EcsEventWorld _eventWorld = new();
    private EcsMetaWorld _metaWorld = new();
    
    public void OnRegisterServices(IServiceRegister services)
    {
        services
            .Register(_world)
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

    public void OnHotReload(IModule oldModule)
    {
        if (oldModule is ECSInstaller oldInstaller)
        {
            _world = oldInstaller._world;
            _eventWorld = oldInstaller._eventWorld;
            _metaWorld = oldInstaller._metaWorld;
        }
    }
}