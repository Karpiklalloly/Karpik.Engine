using System.Drawing;
using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public struct DrawTextCmd
{
    public IFont Font;
    public ReadOnlyMemory<char> Text;
    public Vector2 Position;
    public float Size;
    public Color Color;
}