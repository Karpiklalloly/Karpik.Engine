using Karpik.Engine.Client.Graphics.Core;

namespace Karpik.Engine.Client.Graphics.OpenGL;

public readonly struct RenderView
{
    public readonly Camera2D Camera;
    public readonly uint FramebufferWidth;
    public readonly uint FramebufferHeight;

    public RenderView(
        in Camera2D camera,
        uint framebufferWidth,
        uint framebufferHeight)
    {
        Camera = camera;
        FramebufferWidth = framebufferWidth;
        FramebufferHeight = framebufferHeight;
    }
}