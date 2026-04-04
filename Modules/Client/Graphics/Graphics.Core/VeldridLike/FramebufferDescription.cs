namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public readonly struct FramebufferDescription
{
    public readonly ITexture? DepthTarget;
    public readonly ReadOnlyMemory<ITexture> ColorTargets; // Используем Memory, чтобы избежать выделения массивов
    
    public FramebufferDescription(ITexture? depthTarget, params ITexture[] colorTargets)
    {
        DepthTarget = depthTarget;
        ColorTargets = colorTargets;
    }
}