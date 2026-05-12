using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using StbImageSharp;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core.AssetManagement;

public class TextureLoader : BaseAssetLoader<TextureAsset, ITexture2D>, IOnInjectedDI
{
    public override string? DefaultPath => "Sprites/default.jpg";
    public override string[] SupportedExtensions => [".jpg", ".png", ".bmp", ".tga", ".psd", ".gif", ".hdr"];

    [DI] private GraphicsDevice _device = null!;
    private ResourceFactory _factory = null!;
    
    public void OnInjected()
    {
        _factory = _device.ResourceFactory;
    }
    
    protected override JobHandle<ITexture2D?> OnLoadAsync(Stream stream, string assetName)
    {
        return Job.Run<ITexture2D?>(() =>
        {
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            Texture? texture = _factory.CreateTexture(new TextureDescription(
                (uint)image.Width, (uint)image.Height, 1, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled, TextureType.Texture2D));
            _device.UpdateTexture(
                texture, 
                image.Data, 
                0, 0, 0,
                (uint)image.Width, (uint)image.Height, 1,
                0, 0
            );

            TextureView? view = _factory.CreateTextureView(texture);
            Sampler? sampler = _factory.CreateSampler(SamplerDescription.Linear);
            ResourceLayout? layout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("Samp", ResourceKind.Sampler, ShaderStages.Fragment)
            ));

            ResourceSet set = _factory.CreateResourceSet(new ResourceSetDescription(
                layout, view, sampler));
            
            VeldridTexture2D? texture2D = new(texture, set);

            return texture2D;
        });
    }

    protected override TextureAsset EmptyAsset() => new();

    protected override void SetValue(TextureAsset asset, ITexture2D value)
    {
        asset.Texture = value;
    }
}
