using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class VeldridTextureFactory(GraphicsDevice device) : ITextureFactory
{
    private readonly ResourceFactory _factory = device.ResourceFactory;

    public ITexture2D Create(TextureDescription description)
    {
        Texture? texture = _factory.CreateTexture(description);
        if (texture is null)
        {
            throw new NullReferenceException($"Texture is null!");
        }
        return new VeldridTexture2D(texture);
    }

    public void Destroy(ITexture2D texture)
    {
        texture.Dispose();
    }
}