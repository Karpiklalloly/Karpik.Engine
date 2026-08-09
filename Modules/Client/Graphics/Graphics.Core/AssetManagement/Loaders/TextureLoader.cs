using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using StbImageSharp;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core.AssetManagement;

[Export(typeof(IAssetLoader))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class TextureLoader : BaseAssetLoader<TextureAsset, ITexture2D>
{
    public override string? DefaultPath => "Sprites/default.jpg";
    public override string[] SupportedExtensions => [".jpg", ".png", ".bmp", ".tga", ".psd", ".gif", ".hdr"];

    // TODO: Убрать нулабл отсюда, временное решение для перехода на DI
    private readonly GraphicsDevice? _device;
    private readonly ResourceFactory? _factory;

    public TextureLoader(GraphicsDevice? device = null)
    {
        _device = device;
        _factory = _device?.ResourceFactory;
    }
    
    protected override JobHandle<ITexture2D?> OnLoadAsync(IAssetLoadContext context, Stream stream, string assetName)
    {
        if (_device is null || _factory is null)
        {
            throw new NotSupportedException("Texture loading is unavailable. No GraphicsDevice or ResourceFactory");
            return JobHandle<ITexture2D?>.FromResult(null);
        }

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
            
            VeldridTexture2D texture2D = new(texture, set);

            return texture2D;
        });
    }

    protected override TextureAsset EmptyAsset() => new();

    protected override void SetValue(IAssetLoadContext context, TextureAsset asset, ITexture2D value)
    {
        asset.Texture = value;
    }
}
