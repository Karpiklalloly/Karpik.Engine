using System.Drawing;
using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public struct DrawTextCmd
{
    public IFont Font;
    public ReadOnlyMemory<char> Text;
    public Vector2 Position;
    public Vector2 Origin;
    public float Size;
    public float RotationRadians;
    public Color Color;
    public DrawSpace Space;
}
