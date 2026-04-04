namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public interface IFramebuffer : IGraphicsResource
{
    uint Width { get; }
    uint Height { get; }
    ReadOnlySpan<ITexture> ColorTargets { get; }
    ITexture? DepthTarget { get; }
}