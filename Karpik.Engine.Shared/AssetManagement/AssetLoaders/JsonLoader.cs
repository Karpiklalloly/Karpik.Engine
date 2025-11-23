namespace Karpik.Engine.Shared;

public class JsonLoader<T> : IAssetLoader where T : Asset, new()
{
    protected JsonSerializer Serializer { get; } = new();

    public async Task<Asset> LoadAsync(Stream stream, string assetName)
    {
        return await Task.Run(() =>
        {
            using var reader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(reader);
            return Serializer.Deserialize<T>(jsonReader);
        });
    }
}