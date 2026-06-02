using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public readonly struct TextLayoutResult
{
    public readonly int GlyphCount;
    public readonly Vector2 Size;
    public readonly bool IsTruncated;

    public TextLayoutResult(int glyphCount, Vector2 size, bool isTruncated)
    {
        GlyphCount = glyphCount;
        Size = size;
        IsTruncated = isTruncated;
    }
}
