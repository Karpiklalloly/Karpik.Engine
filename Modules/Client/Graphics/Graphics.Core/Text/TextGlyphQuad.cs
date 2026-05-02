using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public readonly struct TextGlyphQuad
{
    public readonly Vector2 Position;
    public readonly Vector2 Size;
    public readonly Vector4 SourceUv;

    public TextGlyphQuad(Vector2 position, Vector2 size, Vector4 sourceUv)
    {
        Position = position;
        Size = size;
        SourceUv = sourceUv;
    }
}
