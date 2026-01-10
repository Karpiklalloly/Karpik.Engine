using Karpik.Engine.Core;
using Newtonsoft.Json;

namespace Karpik.Engine.Shared.AssetManagement.Base;

public abstract class JsonLoader<TAsset, TValue> : BaseAssetLoader<TAsset, TValue> where TAsset : Asset, new()
{
    public override string[] SupportedExtensions { get; } = [".json"];
    protected JsonSerializer Serializer { get; } = new();

    protected override async JobHandle<TValue?> OnLoadAsync(Stream stream, string assetName)
    {
        return await Job.Run(() =>
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            using var jsonReader = new JsonTextReader(reader);
            return Serializer.Deserialize<TValue>(jsonReader);
        });
    }
}