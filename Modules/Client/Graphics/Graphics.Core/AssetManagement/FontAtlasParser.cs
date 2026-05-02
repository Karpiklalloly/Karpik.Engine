using System.Numerics;
using System.Text.Json;

namespace Karpik.Engine.Client.Graphics.Core.AssetManagement;

internal static class FontAtlasParser
{
    public static FontAtlasData Parse(ReadOnlySpan<byte> data)
    {
        Utf8JsonReader reader = new Utf8JsonReader(data, isFinalBlock: true, state: default);
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        string atlas = root.GetProperty("atlas").GetString()
                       ?? throw new InvalidDataException("Font atlas metadata is missing atlas path.");
        float size = root.GetProperty("size").GetSingle();
        float lineHeight = root.GetProperty("lineHeight").GetSingle();
        float ascender = root.GetProperty("ascender").GetSingle();
        float descender = root.GetProperty("descender").GetSingle();
        float distanceRange = root.GetProperty("distanceRange").GetSingle();
        float atlasWidth = root.GetProperty("atlasWidth").GetSingle();
        float atlasHeight = root.GetProperty("atlasHeight").GetSingle();

        JsonElement glyphElements = root.GetProperty("glyphs");
        FontGlyph[] glyphs = new FontGlyph[glyphElements.GetArrayLength()];
        int glyphIndex = 0;

        foreach (JsonElement glyphElement in glyphElements.EnumerateArray())
        {
            uint codepoint = glyphElement.GetProperty("codepoint").GetUInt32();
            float x = glyphElement.GetProperty("x").GetSingle();
            float y = glyphElement.GetProperty("y").GetSingle();
            float width = glyphElement.GetProperty("width").GetSingle();
            float height = glyphElement.GetProperty("height").GetSingle();
            float bearingX = glyphElement.GetProperty("bearingX").GetSingle();
            float bearingY = glyphElement.GetProperty("bearingY").GetSingle();
            float advance = glyphElement.GetProperty("advance").GetSingle();

            Vector4 sourceUv = new Vector4(
                x / atlasWidth,
                y / atlasHeight,
                (x + width) / atlasWidth,
                (y + height) / atlasHeight);

            glyphs[glyphIndex++] = new FontGlyph(
                codepoint,
                new Vector2(width, height),
                new Vector2(bearingX, bearingY),
                advance,
                sourceUv);
        }

        FontAtlasMetrics metrics = new FontAtlasMetrics(size, lineHeight, ascender, descender, distanceRange);
        return new FontAtlasData(atlas, in metrics, glyphs);
    }
}
