namespace Karpik.Engine.Client.Graphics.Core;

public readonly struct FontAtlasMetrics
{
    public readonly float Size;
    public readonly float LineHeight;
    public readonly float Ascender;
    public readonly float Descender;
    public readonly float DistanceRange;

    public FontAtlasMetrics(float size, float lineHeight, float ascender, float descender, float distanceRange)
    {
        Size = size;
        LineHeight = lineHeight;
        Ascender = ascender;
        Descender = descender;
        DistanceRange = distanceRange;
    }
}
