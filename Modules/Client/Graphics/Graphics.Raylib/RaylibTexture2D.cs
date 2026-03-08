using Karpik.Engine.Client.Graphics.Core;
using Raylib_cs;

namespace Karpik.Engine.Client.Graphics.GRaylib;

public class RaylibTexture2D : ITexture2D
{
    public Texture2D Texture { get; }
    
    public RaylibTexture2D(Texture2D texture)
    {
        Texture = texture;
    }

    public int Width => Texture.Width;
    public int Height => Texture.Height;
}