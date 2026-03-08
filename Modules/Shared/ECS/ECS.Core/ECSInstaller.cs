using System.Text;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

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

    public void OnRegisterServices(IServiceRegister services)
    {
        services
            .Register(_world)
            .Register(new DefaultWorld(_world))
            .Register(_eventWorld)
            .Register(new EventWorld(_eventWorld))
            .Register(_metaWorld)
            .Register(new MetaWorld(_metaWorld));
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
        container.Register<IEcsModule>(new ECSModule());
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }

    public byte[] OnPrepareHotReload(IServiceContainer services)
    {
        _snapshotDefault = _world.Snapshot;
        _snapshotEvent = _eventWorld.Snapshot;
        _snapshotMeta = _metaWorld.Snapshot;

        _reloaded = false;
        ToTemplateExtensions.Clear();
        ToTemplateExtensions2.Clear();
        EcsStaticCleaner.ResetAll();

        _world.Destroy();
        _world = null!;
        _eventWorld.Destroy();
        _eventWorld = null!;
        _metaWorld.Destroy();
        _metaWorld = null!;
        string json = JsonConvert.SerializeObject(new HotReloadInfo()
            {
                EcsDefaultWorldJson = _snapshotDefault,
                EcsEventWorldJson = _snapshotEvent,
                EcsMetaWorldJson = _snapshotMeta
            },
            new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Objects,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                Converters = [new ComponentArrayConverter()],
                ContractResolver = new DefaultContractResolver()
            });

        return Encoding.UTF8.GetBytes(json);
    }

    public bool OnHotReload(byte[] data, IServiceContainer services)
    {
        if (!_reloaded)
        {
            _reloaded = true;
            return false;
        }

        var hotReloadData = JsonConvert.DeserializeObject<HotReloadInfo>(Encoding.UTF8.GetString(data));
        services.Get<MainThreadScheduler>().Schedule(() =>
        {
            EcsWorld.FromSnapshot(_world, hotReloadData!.EcsDefaultWorldJson, services);
            EcsWorld.FromSnapshot(_eventWorld, hotReloadData.EcsEventWorldJson, services);
            EcsWorld.FromSnapshot(_metaWorld, hotReloadData.EcsMetaWorldJson, services);
        });

        return true;
    }
}
