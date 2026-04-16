using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public interface ITextureFactory
{
    public ITexture2D Create(TextureDescription description);

    public void Destroy(ITexture2D texture);
}