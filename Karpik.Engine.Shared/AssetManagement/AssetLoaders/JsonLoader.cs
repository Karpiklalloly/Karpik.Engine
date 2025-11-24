namespace Karpik.Engine.Shared;

public class JsonLoader<T> : BaseAssetLoader<T> where T : Asset, new()
{
    public override string[] SupportedExtensions { get; } = [".json"];
    public Type AssetType => typeof(T);

    protected JsonSerializer Serializer { get; } = new();
    [DI] protected AssetsManager AssetsManager { get; }

    protected override async Task<T> OnLoadAsync(Stream stream, string assetName)
    {
        return await Task.Run(() =>
        {
            using var reader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(reader);
            var asset = Serializer.Deserialize<T>(jsonReader);
            return asset;
        });
    }
}