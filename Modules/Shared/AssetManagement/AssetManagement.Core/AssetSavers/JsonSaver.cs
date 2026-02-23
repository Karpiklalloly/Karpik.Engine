using System.Text;
using Karpik.Engine.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Karpik.Engine.Shared.AssetManagement.Core;

public abstract class JsonSaver<TAsset> : BaseAssetSaver<TAsset> where TAsset : Asset
{
    protected JsonSerializer Serializer { get; } = new();

    public JsonSaver()
    {
        Serializer.Formatting = Formatting.Indented;
        Serializer.ContractResolver = new DefaultContractResolver();
    }

    protected override async JobHandle OnSaveAsync(TAsset asset, Stream stream)
    {
        await Job.Run(() =>
        {
            using var sw = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(sw);
            Serializer.Serialize(jsonWriter, asset.RawValue, AssetType);
        });
    }
}