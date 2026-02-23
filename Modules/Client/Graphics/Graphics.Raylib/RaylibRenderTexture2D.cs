using Karpik.Engine.Client.Graphics.Core;
using Raylib_cs;

namespace Karpik.Engine.Client.Graphics.GRaylib;

public class RaylibRenderTexture2D : IRenderTexture2D
{
    public RenderTexture2D RenderTexture;
    public ITexture2D Texture => new RaylibTexture2D(RenderTexture.Texture);
}