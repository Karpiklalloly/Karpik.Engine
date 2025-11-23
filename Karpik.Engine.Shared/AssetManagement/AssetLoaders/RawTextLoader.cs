using Karpik.Engine.Shared.Assets;

namespace Karpik.Engine.Shared;

public class RawTextLoader : IAssetLoader
{
    public async Task<Asset> LoadAsync(Stream stream, string assetName)
    {
        using StreamReader reader = new StreamReader(stream);
        string content = await reader.ReadToEndAsync();
        return new TextAsset { Text = content };
    }
}