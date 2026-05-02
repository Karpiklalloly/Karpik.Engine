using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public static class TextLayout
{
    public static TextLayoutResult Build(
        IFont font,
        ReadOnlySpan<char> text,
        float size,
        Span<TextGlyphQuad> output)
    {
        if (size <= 0f || font.Size <= 0f)
        {
            return new TextLayoutResult(0, default, text.Length > 0 && output.Length == 0);
        }

        float scale = size / font.Size;
        float penX = 0f;
        float penY = 0f;
        float maxX = 0f;
        int glyphCount = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                if (penX > maxX)
                {
                    maxX = penX;
                }

                penX = 0f;
                penY += font.LineHeight * scale;
                continue;
            }

            if (!font.TryGetGlyph(c, out FontGlyph glyph))
            {
                continue;
            }

            if (glyphCount >= output.Length)
            {
                return new TextLayoutResult(glyphCount, new Vector2(MathF.Max(maxX, penX), penY + font.LineHeight * scale), true);
            }

            Vector2 position = new Vector2(
                penX + glyph.Bearing.X * scale,
                penY + (font.Ascender - glyph.Bearing.Y) * scale);
            Vector2 glyphSize = glyph.Size * scale;

            output[glyphCount++] = new TextGlyphQuad(position, glyphSize, glyph.SourceUv);
            penX += glyph.Advance * scale;

            float glyphRight = position.X + glyphSize.X;
            if (glyphRight > maxX)
            {
                maxX = glyphRight;
            }
        }

        return new TextLayoutResult(glyphCount, new Vector2(MathF.Max(maxX, penX), penY + font.LineHeight * scale), false);
    }
}
