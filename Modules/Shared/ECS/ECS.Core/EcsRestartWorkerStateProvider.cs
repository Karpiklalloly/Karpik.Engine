using System.Composition;
using System.Text;
using Karpik.Engine.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Karpik.Engine.Shared.ECS;

[Export(typeof(IRestartWorkerStateProvider))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class EcsRestartWorkerStateProvider(
    EcsDefaultWorld world,
    EcsEventWorld eventWorld,
    EcsMetaWorld metaWorld,
    IServiceResolver resolver,
    ILogger<ComponentArrayConverter> logger) : IRestartWorkerStateProvider
{
    public string Key => "ECS";

    private ComponentArrayConverter _converter = new(logger);

    public byte[] Capture()
    {
        var snapshotDefault = world.ToSnapshot(_converter);
        var snapshotEvent = eventWorld.ToSnapshot(_converter);
        var snapshotMeta = metaWorld.ToSnapshot(_converter);

        ToTemplateExtensions.Clear();
        ToTemplateExtensions2.Clear();
        TypeMeta.ClearCache();
        EcsAspect.ClearCache();
        string json = JsonConvert.SerializeObject(new HotReloadInfo()
            {
                EcsDefaultWorldJson = snapshotDefault,
                EcsEventWorldJson = snapshotEvent,
                EcsMetaWorldJson = snapshotMeta
            },
            new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Objects,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                Converters = [new ComponentArrayConverter(logger)],
                ContractResolver = new DefaultContractResolver()
            });

        return Encoding.UTF8.GetBytes(json);
    }

    public void Restore(ReadOnlySpan<byte> data)
    {
        var hotReloadData = JsonConvert.DeserializeObject<HotReloadInfo>(Encoding.UTF8.GetString(data));
        EcsWorld.FromSnapshot(world, hotReloadData!.EcsDefaultWorldJson, resolver, _converter).GetAwaiter().GetResult();
        EcsWorld.FromSnapshot(eventWorld, hotReloadData.EcsEventWorldJson, resolver, _converter).GetAwaiter().GetResult();
        EcsWorld.FromSnapshot(metaWorld, hotReloadData.EcsMetaWorldJson, resolver, _converter).GetAwaiter().GetResult();
    }
}