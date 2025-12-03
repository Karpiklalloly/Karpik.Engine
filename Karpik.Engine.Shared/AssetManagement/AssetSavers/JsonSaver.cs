using System.Text;

namespace Karpik.Engine.Shared;

public abstract class JsonSaver<TAsset> : BaseAssetSaver<TAsset> where TAsset : Asset
{
    protected JsonSerializer Serializer { get; } = new();

    public JsonSaver()
    {
        Serializer.Formatting = Formatting.Indented; 
    }

    protected override async Task OnSaveAsync(TAsset asset, Stream stream)
    {
        await Task.Run(() =>
        {
            using var sw = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(sw);
            Serializer.Serialize(jsonWriter, asset.RawValue, AssetType);
        });
    }
}