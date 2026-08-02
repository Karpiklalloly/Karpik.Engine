using System.ComponentModel.Design;
using System.Text;
using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Karpik.Engine.Shared.ECS;

[Module]
public class EcsModuleInstaller : IModuleInstaller, IModuleInstallerHotReload, IModuleInstallerConfiguratable
{
    public string Name => "ECS.Core";
    
    private EcsDefaultWorld _world = new();
    private EcsEventWorld _eventWorld = new();
    private EcsMetaWorld _metaWorld = new();

    private string _snapshotDefault = string.Empty;
    private string _snapshotEvent = string.Empty;
    private string _snapshotMeta = string.Empty;

    private bool _reloaded = false;

    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder
            .Register(_world)
            .Register(new DefaultWorld(_world, serviceContainer))
            .Register(_eventWorld)
            .Register(new EventWorld(_eventWorld, serviceContainer))
            .Register(_metaWorld)
            .Register(new MetaWorld(_metaWorld, serviceContainer));
    }

    public void OnConfigure(IServiceResolver services, out IModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceResolver services)
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
        TypeMeta.ClearCache();
        EcsAspect.ClearCache();

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

    public bool OnHotReload(byte[] data, IServiceResolver services)
    {
        if (!_reloaded)
        {
            _reloaded = true;
            return false;
        }

        var hotReloadData = JsonConvert.DeserializeObject<HotReloadInfo>(Encoding.UTF8.GetString(data));
        EcsWorld.FromSnapshot(_world, hotReloadData!.EcsDefaultWorldJson, services).GetAwaiter().GetResult();
        EcsWorld.FromSnapshot(_eventWorld, hotReloadData.EcsEventWorldJson, services).GetAwaiter().GetResult();
        EcsWorld.FromSnapshot(_metaWorld, hotReloadData.EcsMetaWorldJson, services).GetAwaiter().GetResult();

        return true;
    }
}
