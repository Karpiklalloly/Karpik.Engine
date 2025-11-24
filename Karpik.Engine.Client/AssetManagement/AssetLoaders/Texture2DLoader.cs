using Karpik.Engine.Client.AssetManagement.Assets;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class Texture2DLoader : BaseAssetLoader<Texture2DAsset>
{
    public override string[] SupportedExtensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif", ".psd", ".hdr", ".pic", ".pvr", ".webp"];

    protected override Task<Texture2DAsset> OnLoadAsync(Stream stream, string assetName)
    {
        stream.Close();
        var texture = Raylib.LoadTexture(assetName);
        return Task.FromResult(new Texture2DAsset(texture));
    }
}