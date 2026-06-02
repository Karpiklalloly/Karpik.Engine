namespace Karpik.Engine.Client.Graphics.Core;

public sealed class AtlasFont : IFont
{
    private readonly FontGlyph[] _glyphs;
    private readonly bool _ownsAtlasTexture;

    public ITexture2D AtlasTexture { get; }
    public float Size { get; }
    public float LineHeight { get; }
    public float Ascender { get; }
    public float Descender { get; }
    public float DistanceRange { get; }

    public AtlasFont(ITexture2D atlasTexture, in FontAtlasMetrics metrics, FontGlyph[] glyphs, bool ownsAtlasTexture = true)
    {
        AtlasTexture = atlasTexture;
        Size = metrics.Size;
        LineHeight = metrics.LineHeight;
        Ascender = metrics.Ascender;
        Descender = metrics.Descender;
        DistanceRange = metrics.DistanceRange;
        _glyphs = glyphs;
        Array.Sort(_glyphs, static (left, right) => left.Codepoint.CompareTo(right.Codepoint));
        _ownsAtlasTexture = ownsAtlasTexture;
    }

    public bool TryGetGlyph(uint codepoint, out FontGlyph glyph)
    {
        int left = 0;
        int right = _glyphs.Length - 1;

        while (left <= right)
        {
            int middle = left + ((right - left) >> 1);
            uint middleCodepoint = _glyphs[middle].Codepoint;
            if (middleCodepoint == codepoint)
            {
                glyph = _glyphs[middle];
                return true;
            }

            if (middleCodepoint < codepoint)
            {
                left = middle + 1;
                continue;
            }

            right = middle - 1;
        }

        glyph = default;
        return false;
    }

    public void Dispose()
    {
        if (_ownsAtlasTexture)
        {
            AtlasTexture.Dispose();
        }
    }
}
