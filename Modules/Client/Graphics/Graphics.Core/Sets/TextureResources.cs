using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core.Sets;

public class TextureResources
{
    public ResourceSet WhiteRectResourceSet { get; private set; }
    public Texture WhiteTexture { get; private set; }
    public TextureView WhiteTextureView { get; private set; }

    public void Init(GraphicsDevice device, ResourceLayout mainLayout)
    {
        ResourceFactory factory = device.ResourceFactory;

        WhiteTexture = factory.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        
        RgbaByte[] whitePixels = [RgbaByte.White];
        device.UpdateTexture(
            WhiteTexture, 
            whitePixels, 
            0, 0, 0,
            1, 1, 1,
            0, 0
        );

        WhiteTextureView = factory.CreateTextureView(WhiteTexture);

        var rsDesc = new ResourceSetDescription(
            mainLayout,
            WhiteTextureView,
            device.PointSampler
        );
        
        WhiteRectResourceSet = factory.CreateResourceSet(ref rsDesc);
    }
}