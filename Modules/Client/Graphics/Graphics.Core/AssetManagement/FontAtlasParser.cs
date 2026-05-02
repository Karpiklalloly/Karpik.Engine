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

        JsonElement atlasElement = root.GetProperty("atlas");
        if (atlasElement.ValueKind == JsonValueKind.Object)
        {
            return ParseMsdfAtlasGen(root, atlasElement);
        }

        return ParseKarpikFontAtlas(root, atlasElement);
    }

    private static FontAtlasData ParseKarpikFontAtlas(JsonElement root, JsonElement atlasElement)
    {
        string atlas = atlasElement.GetString()
                       ?? throw new InvalidDataException("Font atlas metadata has an invalid atlas path.");
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

    private static FontAtlasData ParseMsdfAtlasGen(JsonElement root, JsonElement atlasElement)
    {
        float size = atlasElement.GetProperty("size").GetSingle();
        float distanceRange = atlasElement.GetProperty("distanceRange").GetSingle();
        float atlasWidth = atlasElement.GetProperty("width").GetSingle();
        float atlasHeight = atlasElement.GetProperty("height").GetSingle();
        string yOrigin = atlasElement.TryGetProperty("yOrigin", out JsonElement yOriginElement)
            ? yOriginElement.GetString() ?? "bottom"
            : "bottom";

        JsonElement metricsElement = root.GetProperty("metrics");
        float emSize = metricsElement.TryGetProperty("emSize", out JsonElement emSizeElement)
            ? emSizeElement.GetSingle()
            : 1f;
        float unitsToPixels = emSize != 0f ? size / emSize : size;

        FontAtlasMetrics metrics = new FontAtlasMetrics(
            size,
            metricsElement.GetProperty("lineHeight").GetSingle() * unitsToPixels,
            metricsElement.GetProperty("ascender").GetSingle() * unitsToPixels,
            metricsElement.GetProperty("descender").GetSingle() * unitsToPixels,
            distanceRange);

        JsonElement glyphElements = root.GetProperty("glyphs");
        FontGlyph[] glyphs = new FontGlyph[glyphElements.GetArrayLength()];
        int glyphIndex = 0;

        foreach (JsonElement glyphElement in glyphElements.EnumerateArray())
        {
            uint codepoint = glyphElement.GetProperty("unicode").GetUInt32();
            float advance = glyphElement.GetProperty("advance").GetSingle() * unitsToPixels;

            Vector2 sizePixels = default;
            Vector2 bearingPixels = default;
            if (glyphElement.TryGetProperty("planeBounds", out JsonElement planeBounds))
            {
                float left = planeBounds.GetProperty("left").GetSingle();
                float bottom = planeBounds.GetProperty("bottom").GetSingle();
                float right = planeBounds.GetProperty("right").GetSingle();
                float top = planeBounds.GetProperty("top").GetSingle();

                sizePixels = new Vector2((right - left) * unitsToPixels, (top - bottom) * unitsToPixels);
                bearingPixels = new Vector2(left * unitsToPixels, top * unitsToPixels);
            }

            Vector4 sourceUv = default;
            if (glyphElement.TryGetProperty("atlasBounds", out JsonElement atlasBounds))
            {
                sourceUv = GetMsdfAtlasSourceUv(atlasBounds, atlasWidth, atlasHeight, yOrigin);
            }

            glyphs[glyphIndex++] = new FontGlyph(codepoint, sizePixels, bearingPixels, advance, sourceUv);
        }

        return new FontAtlasData(string.Empty, in metrics, glyphs);
    }

    private static Vector4 GetMsdfAtlasSourceUv(JsonElement atlasBounds, float atlasWidth, float atlasHeight, string yOrigin)
    {
        float left = atlasBounds.GetProperty("left").GetSingle();
        float bottom = atlasBounds.GetProperty("bottom").GetSingle();
        float right = atlasBounds.GetProperty("right").GetSingle();
        float top = atlasBounds.GetProperty("top").GetSingle();

        if (string.Equals(yOrigin, "bottom", StringComparison.OrdinalIgnoreCase))
        {
            return new Vector4(
                left / atlasWidth,
                (atlasHeight - top) / atlasHeight,
                right / atlasWidth,
                (atlasHeight - bottom) / atlasHeight);
        }

        return new Vector4(
            left / atlasWidth,
            top / atlasHeight,
            right / atlasWidth,
            bottom / atlasHeight);
    }
}
