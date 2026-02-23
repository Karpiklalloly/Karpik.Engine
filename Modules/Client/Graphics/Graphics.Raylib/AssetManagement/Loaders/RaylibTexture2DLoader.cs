using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.Log;
using Karpik.Jobs;
using Raylib_cs;

namespace Karpik.Engine.Client.Graphics.GRaylib;

public class RaylibTexture2DLoader : BaseAssetLoader<Texture2DAsset, Texture2D>
{
    public override string? DefaultPath => AssetsManager.FileSystem.Combine(AssetsManager.ContentPath, "Sprites", "default.jpg");
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
            await Logger.Instance.Log(nameof(RaylibTexture2DLoader), ex.ToString());
        }
        finally
        {
            Raylib.UnloadImage(image);
        }

        return default;
    }

    protected override Texture2DAsset EmptyAsset() => new RaylibTexture2DAsset()
    {
        TextureRaylib = default
    };

    protected override void SetValue(Texture2DAsset asset, Texture2D value) => ((RaylibTexture2DAsset)asset).TextureRaylib = new RaylibTexture2D(value);
}