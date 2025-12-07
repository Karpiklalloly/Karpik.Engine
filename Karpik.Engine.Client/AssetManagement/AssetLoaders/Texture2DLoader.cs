using Karpik.Engine.Client.AssetManagement.Assets;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class Texture2DLoader : BaseAssetLoader<Texture2DAsset, Texture2D>
{
    public override string DefaultPath => AssetsManager.FileSystem.Combine(AssetsManager.ContentPath, "Sprites", "default.jpg");
    public override string[] SupportedExtensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif", ".psd", ".hdr", ".pic", ".pvr", ".webp"];
    
    protected override async JobHandle<Texture2D> OnLoadAsync(Stream stream, string assetName)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var image = Raylib.LoadImageFromMemory(Path.GetExtension(assetName), ms.ToArray());
        
        try
        {
            var texture = await MainThreadScheduler.InvokeAsync(() => Raylib.LoadTextureFromImage(image));
            return texture;
        }
        catch (Exception ex)
        {
            await Logger.Instance.Log(nameof(Texture2DLoader), ex.ToString());
        }
        finally
        {
            Raylib.UnloadImage(image);
        }

        return default;
    }

    protected override Texture2DAsset EmptyAsset() => new();

    protected override void SetValue(Texture2DAsset asset, Texture2D value) => asset.Texture = value;
}