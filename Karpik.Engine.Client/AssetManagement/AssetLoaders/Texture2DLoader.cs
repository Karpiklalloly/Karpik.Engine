using Karpik.Engine.Client.AssetManagement.Assets;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class Texture2DLoader : IAssetLoader
{
    public string[] SupportedExtensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif", ".psd", ".hdr", ".pic", ".pvr", ".webp"];
    public Task<Asset> LoadAsync(Stream stream, string assetName)
    {
        stream.Close();
        var texture = Raylib.LoadTexture(assetName);
        return Task.FromResult<Asset>(new Texture2DAsset(texture));
    }
}