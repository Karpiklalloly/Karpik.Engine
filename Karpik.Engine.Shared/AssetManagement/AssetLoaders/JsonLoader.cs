namespace Karpik.Engine.Shared;

public class JsonLoader<T> : IAssetLoader where T : Asset, new()
{
    public virtual string[] SupportedExtensions { get; } = [".json"];
    
    protected JsonSerializer Serializer { get; } = new();
    [DI] protected AssetsManager AssetsManager { get; }

    public async Task<Asset> LoadAsync(Stream stream, string assetName)
    {
        return await Task.Run(() =>
        {
            using var reader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(reader);
            var asset = Serializer.Deserialize<T>(jsonReader);
            OnAssetLoadedAsync(asset);
            return asset;
        });
    }

    protected virtual Task OnAssetLoadedAsync(T asset) => Task.CompletedTask;
}