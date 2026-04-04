namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public interface ITexture : IGraphicsResource
{
    uint Width { get; }
    uint Height { get; }
    PixelFormat Format { get; }
}