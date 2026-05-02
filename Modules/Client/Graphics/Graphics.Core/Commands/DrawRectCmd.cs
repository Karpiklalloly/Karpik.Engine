using System.Drawing;
using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public struct DrawRectCmd
{
    public RectangleF Rectangle;
    public Color Color;
    public Vector2 Origin;
    public float RotationRadians;
    public DrawSpace Space;
}
