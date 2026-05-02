using System.Drawing;
using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public struct DrawTextureCmd
{
    public ITexture2D Texture;
    public Vector2 Position;
    public Color Color;
    public Vector2 Size;
    public Vector2 Origin;
    public float RotationRadians;
    public DrawSpace Space;
}
