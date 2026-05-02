using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public readonly struct FontGlyph
{
    public readonly uint Codepoint;
    public readonly Vector2 Size;
    public readonly Vector2 Bearing;
    public readonly float Advance;
    public readonly Vector4 SourceUv;

    public FontGlyph(uint codepoint, Vector2 size, Vector2 bearing, float advance, Vector4 sourceUv)
    {
        Codepoint = codepoint;
        Size = size;
        Bearing = bearing;
        Advance = advance;
        SourceUv = sourceUv;
    }
}
