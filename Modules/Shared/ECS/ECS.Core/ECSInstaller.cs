using System.Reflection;
using DCFApixels.DragonECS.Core.Internal;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Hot;
using Karpik.Engine.Core.ModuleManagement;
using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Shared.ECS;

[Module]
public class ECSInstaller : IModule, IModuleHotReload, IModuleConfiguratable
{
    public string Name => "ECS.Core";
    
    private EcsDefaultWorld _world = new();
    private EcsEventWorld _eventWorld = new();
    private EcsMetaWorld _metaWorld = new();

    private string _snapshotDefault = string.Empty;
    private string _snapshotEvent = string.Empty;
    private string _snapshotMeta = string.Empty;

    private bool _reloaded = false;
    private EcsPipeline.Builder _builder = null!;

    public void OnRegisterServices(IServiceRegister services)
    {
        _builder = EcsPipeline.New();
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
        ToTemplateExtensions.Clear();
        ToTemplateExtensions2.Clear();

        _world.Destroy();
        _world = null!;
        _eventWorld.Destroy();
        _eventWorld = null!;
        _metaWorld.Destroy();
        _metaWorld = null!;
    }

    public bool OnHotReload(IModule oldModule, IServiceContainer services)
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

        var assetManager = services.Get<IAssetsManager>();
        services.Get<MainThreadScheduler>().Schedule(() =>
        {
            EcsWorld.FromSnapshot(_world, snapshotDefault, assetManager);
            EcsWorld.FromSnapshot(_eventWorld, snapshotEvent, assetManager);
            EcsWorld.FromSnapshot(_metaWorld, snapshotMeta, assetManager);
        });

        return true;
    }
}
