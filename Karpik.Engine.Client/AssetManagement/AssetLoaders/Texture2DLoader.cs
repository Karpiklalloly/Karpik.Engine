using Karpik.Engine.Client.AssetManagement.Assets;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class Texture2DLoader : BaseAssetLoader<Texture2DAsset, Texture2D>
{
    public override string[] SupportedExtensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif", ".psd", ".hdr", ".pic", ".pvr", ".webp"];

    protected override async Task<Texture2D> OnLoadAsync(Stream stream, string assetName)
    {
        return await Task.Run(async () =>
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            string ext = Path.GetExtension(assetName);
            Image img = Raylib.LoadImageFromMemory(ext, ms.ToArray());
            var texture = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            return texture;
        });
    }

    protected override Texture2DAsset EmptyAsset() => new();

    protected override void SetValue(Texture2DAsset asset, Texture2D value) => asset.Texture = value;
}