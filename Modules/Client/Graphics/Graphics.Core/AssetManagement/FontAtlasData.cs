namespace Karpik.Engine.Client.Graphics.Core.AssetManagement;

internal readonly struct FontAtlasData
{
    public readonly string AtlasPath;
    public readonly FontAtlasMetrics Metrics;
    public readonly FontGlyph[] Glyphs;

    public FontAtlasData(string atlasPath, in FontAtlasMetrics metrics, FontGlyph[] glyphs)
    {
        AtlasPath = atlasPath;
        Metrics = metrics;
        Glyphs = glyphs;
    }
}
