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
        _ownsAtlasTexture = ownsAtlasTexture;
    }

    public bool TryGetGlyph(uint codepoint, out FontGlyph glyph)
    {
        for (int i = 0; i < _glyphs.Length; i++)
        {
            if (_glyphs[i].Codepoint == codepoint)
            {
                glyph = _glyphs[i];
                return true;
            }
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
