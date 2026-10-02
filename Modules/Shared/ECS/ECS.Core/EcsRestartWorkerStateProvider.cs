using System.Composition;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Karpik.Engine.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Karpik.Engine.Shared.ECS;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(HotReloadInfo))]
internal partial class HotReloadInfoJsonContext : JsonSerializerContext;

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
        // The envelope is source-generated (NativeAOT-safe); only the inner
        // world-snapshot strings still travel through the Newtonsoft pipeline.
        string json = System.Text.Json.JsonSerializer.Serialize(
            new HotReloadInfo()
            {
                EcsDefaultWorldJson = snapshotDefault,
                EcsEventWorldJson = snapshotEvent,
                EcsMetaWorldJson = snapshotMeta
            },
            HotReloadInfoJsonContext.Default.HotReloadInfo);

        return Encoding.UTF8.GetBytes(json);
    }

    public void Restore(ReadOnlySpan<byte> data)
    {
        var hotReloadData =
            System.Text.Json.JsonSerializer.Deserialize(Encoding.UTF8.GetString(data), HotReloadInfoJsonContext.Default.HotReloadInfo)!;
        EcsWorld.FromSnapshot(world, hotReloadData!.EcsDefaultWorldJson, resolver, _converter, logger).GetAwaiter().GetResult();
        EcsWorld.FromSnapshot(eventWorld, hotReloadData.EcsEventWorldJson, resolver, _converter, logger).GetAwaiter().GetResult();
        EcsWorld.FromSnapshot(metaWorld, hotReloadData.EcsMetaWorldJson, resolver, _converter, logger).GetAwaiter().GetResult();
    }
}
