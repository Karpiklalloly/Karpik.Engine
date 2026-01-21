using System.Reflection;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Hot;
using Karpik.Engine.Core.ModuleManagement;

namespace Karpik.Engine.Shared.ECS;

[Module]
public class ECSInstaller : IModule, IModuleHotReload
{
    public string Name => "ECS.Core";
    
    private EcsDefaultWorld _world = new();
    private EcsEventWorld _eventWorld = new();
    private EcsMetaWorld _metaWorld = new();

    private string _snapshotDefault = string.Empty;
    private string _snapshotEvent = string.Empty;
    private string _snapshotMeta = string.Empty;

    private bool _reloaded = false;
    
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

    public void OnPrepareHotReload()
    {
        _snapshotDefault = _world.Snapshot;
        _snapshotEvent = _eventWorld.Snapshot;
        _snapshotMeta = _metaWorld.Snapshot;

        _reloaded = false;
    }

    public bool OnHotReload(IModule oldModule, TypeMapper map)
    {
        if (!_reloaded)
        {
            _reloaded = true;
            return false;
        }
        var oldType = oldModule.GetType();
        var propertyDefault = oldType.GetField(nameof(_snapshotDefault), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var propertyEvent = oldType.GetField(nameof(_snapshotEvent), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var propertyMeta = oldType.GetField(nameof(_snapshotMeta), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        
        var snapshotDefault = (string)propertyDefault!.GetValue(oldModule)!;
        var snapshotEvent = (string)propertyEvent!.GetValue(oldModule)!;
        var snapshotMeta = (string)propertyMeta!.GetValue(oldModule)!;
        
        _world = EcsWorld.FromSnapshot<EcsDefaultWorld>(snapshotDefault, map);
        _eventWorld = EcsWorld.FromSnapshot<EcsEventWorld>(snapshotEvent, map);
        _metaWorld = EcsWorld.FromSnapshot<EcsMetaWorld>(snapshotMeta, map);

        return true;
    }
}
