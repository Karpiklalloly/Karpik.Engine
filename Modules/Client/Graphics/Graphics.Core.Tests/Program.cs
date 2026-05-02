using System.Drawing;
using System.Numerics;
using System.Text;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;

var tests = new (string Name, Action Run)[]
{
    ("Camera_WorldToScreen_MapsCameraPositionToViewportCenter", Camera_WorldToScreen_MapsCameraPositionToViewportCenter),
    ("Camera_WorldToScreen_AppliesZoomAndPixelsPerUnit", Camera_WorldToScreen_AppliesZoomAndPixelsPerUnit),
    ("Camera_WorldToScreen_AppliesCameraRotation", Camera_WorldToScreen_AppliesCameraRotation),
    ("Camera_ScreenToWorld_RoundTripsWorldPoint", Camera_ScreenToWorld_RoundTripsWorldPoint),
    ("Camera_Normalized_KeepsPositionWhenViewportFallbackIsUsed", Camera_Normalized_KeepsPositionWhenViewportFallbackIsUsed),
    ("Quad_ScreenZeroRotation_MapsPixelsToClipSpace", Quad_ScreenZeroRotation_MapsPixelsToClipSpace),
    ("Quad_ScreenCenterRotation_RotatesAroundOrigin", Quad_ScreenCenterRotation_RotatesAroundOrigin),
    ("Quad_WorldSpace_UsesCameraPositionAndScale", Quad_WorldSpace_UsesCameraPositionAndScale),
    ("Quad_ScreenSpace_IgnoresCamera", Quad_ScreenSpace_IgnoresCamera),
    ("TextureUv_Default_UsesFullTexture", TextureUv_Default_UsesFullTexture),
    ("TextureUv_SourceRect_MapsCorners", TextureUv_SourceRect_MapsCorners),
    ("CommandBuffer_AddRectCentered_ConvertsCenterToPositionAndOrigin", CommandBuffer_AddRectCentered_ConvertsCenterToPositionAndOrigin),
    ("CommandBuffer_AddTextureCentered_PreservesTransformSpaceAndUv", CommandBuffer_AddTextureCentered_PreservesTransformSpaceAndUv),
    ("CommandBuffer_AddTexture_DefaultsToTopLeftFullTextureScreenSpace", CommandBuffer_AddTexture_DefaultsToTopLeftFullTextureScreenSpace),
    ("CommandBuffer_AddText_DefaultsToTopLeftScreenSpace", CommandBuffer_AddText_DefaultsToTopLeftScreenSpace),
    ("CommandBuffer_AddTextCentered_UsesMeasuredSizeAsOrigin", CommandBuffer_AddTextCentered_UsesMeasuredSizeAsOrigin),
    ("FontAtlasParser_Parse_NormalizesGlyphUvAndMetrics", FontAtlasParser_Parse_NormalizesGlyphUvAndMetrics),
    ("AtlasFont_TryGetGlyph_FindsExistingAndRejectsMissing", AtlasFont_TryGetGlyph_FindsExistingAndRejectsMissing),
    ("AtlasFont_Dispose_RespectsAtlasTextureOwnership", AtlasFont_Dispose_RespectsAtlasTextureOwnership),
    ("TextLayout_Build_EmitsGlyphQuadsAndSize", TextLayout_Build_EmitsGlyphQuadsAndSize),
    ("TextLayout_Build_HandlesNewlinesMissingGlyphsAndTruncation", TextLayout_Build_HandlesNewlinesMissingGlyphsAndTruncation)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static void Camera_WorldToScreen_MapsCameraPositionToViewportCenter()
{
    Camera2D camera = Camera2D.CreateDefault(800f, 600f);
    camera.Position = new Vector2(10f, 20f);
    camera.PixelsPerUnit = 4f;
    camera.Zoom = 2f;

    Vector2 screen = camera.WorldToScreen(new Vector2(10f, 20f));

    AssertNear(new Vector2(400f, 300f), screen);
}

static void Camera_WorldToScreen_AppliesZoomAndPixelsPerUnit()
{
    Camera2D camera = Camera2D.CreateDefault(800f, 600f);
    camera.Position = new Vector2(10f, 20f);
    camera.PixelsPerUnit = 4f;
    camera.Zoom = 2f;

    Vector2 screen = camera.WorldToScreen(new Vector2(13f, 18f));

    AssertNear(new Vector2(424f, 284f), screen);
}

static void Camera_WorldToScreen_AppliesCameraRotation()
{
    Camera2D camera = Camera2D.CreateDefault(800f, 600f);
    camera.Position = default;
    camera.PixelsPerUnit = 10f;
    camera.Zoom = 1f;
    camera.RotationRadians = MathF.PI * 0.5f;

    Vector2 screen = camera.WorldToScreen(new Vector2(0f, 1f));

    AssertNear(new Vector2(410f, 300f), screen);
}

static void Camera_ScreenToWorld_RoundTripsWorldPoint()
{
    Camera2D camera = Camera2D.CreateDefault(800f, 600f);
    camera.Position = new Vector2(10f, -20f);
    camera.PixelsPerUnit = 8f;
    camera.Zoom = 1.5f;
    camera.RotationRadians = 0.25f;

    Vector2 world = new Vector2(17f, -12f);
    Vector2 screen = camera.WorldToScreen(world);
    Vector2 roundTrip = camera.ScreenToWorld(screen);

    AssertNear(world, roundTrip);
}

static void Camera_Normalized_KeepsPositionWhenViewportFallbackIsUsed()
{
    Camera2D camera = Camera2D.CreateDefault();
    camera.Position = new Vector2(42f, -17f);
    camera.Zoom = 0f;
    camera.PixelsPerUnit = 0f;

    Camera2D normalized = camera.Normalized(800f, 600f);

    AssertNear(new Vector2(42f, -17f), normalized.Position);
    AssertNear(new Vector2(800f, 600f), normalized.ViewportSize);
    AssertNear(new Vector2(400f, 300f), normalized.WorldToScreen(normalized.Position));
}

static void Quad_ScreenZeroRotation_MapsPixelsToClipSpace()
{
    DrawTransform2D transform = new DrawTransform2D(new Vector2(100f, 50f), new Vector2(200f, 100f));

    QuadTransform2D.BuildScreenQuad(in transform, 800f, 600f, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3);

    AssertNear(new Vector2(-0.75f, 0.8333333f), p0);
    AssertNear(new Vector2(-0.25f, 0.8333333f), p1);
    AssertNear(new Vector2(-0.75f, 0.5f), p2);
    AssertNear(new Vector2(-0.25f, 0.5f), p3);
}

static void Quad_ScreenCenterRotation_RotatesAroundOrigin()
{
    DrawTransform2D transform = new DrawTransform2D(
        new Vector2(300f, 200f),
        new Vector2(100f, 50f),
        new Vector2(50f, 25f),
        MathF.PI * 0.5f);

    QuadTransform2D.BuildScreenQuad(in transform, 800f, 600f, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3);

    AssertNear(ToClip(new Vector2(375f, 175f), 800f, 600f), p0);
    AssertNear(ToClip(new Vector2(375f, 275f), 800f, 600f), p1);
    AssertNear(ToClip(new Vector2(325f, 175f), 800f, 600f), p2);
    AssertNear(ToClip(new Vector2(325f, 275f), 800f, 600f), p3);
}

static void Quad_WorldSpace_UsesCameraPositionAndScale()
{
    Camera2D camera = Camera2D.CreateDefault(800f, 600f);
    camera.Position = new Vector2(10f, 0f);
    camera.PixelsPerUnit = 10f;
    camera.Zoom = 1f;

    DrawTransform2D transform = new DrawTransform2D(
        new Vector2(10f, 0f),
        new Vector2(2f, 2f),
        default,
        0f,
        DrawSpace.World);

    QuadTransform2D.BuildQuad(in transform, in camera, 800f, 600f, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3);

    AssertNear(ToClip(new Vector2(400f, 300f), 800f, 600f), p0);
    AssertNear(ToClip(new Vector2(420f, 300f), 800f, 600f), p1);
    AssertNear(ToClip(new Vector2(400f, 320f), 800f, 600f), p2);
    AssertNear(ToClip(new Vector2(420f, 320f), 800f, 600f), p3);
}

static void Quad_ScreenSpace_IgnoresCamera()
{
    Camera2D camera = Camera2D.CreateDefault(800f, 600f);
    camera.Position = new Vector2(1000f, 1000f);
    camera.PixelsPerUnit = 32f;
    camera.Zoom = 3f;

    DrawTransform2D transform = new DrawTransform2D(
        new Vector2(100f, 50f),
        new Vector2(200f, 100f),
        default,
        0f,
        DrawSpace.Screen);

    QuadTransform2D.BuildQuad(in transform, in camera, 800f, 600f, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3);

    AssertNear(new Vector2(-0.75f, 0.8333333f), p0);
    AssertNear(new Vector2(-0.25f, 0.8333333f), p1);
    AssertNear(new Vector2(-0.75f, 0.5f), p2);
    AssertNear(new Vector2(-0.25f, 0.5f), p3);
}

static void TextureUv_Default_UsesFullTexture()
{
    TextureUvTransform.GetTextureCoords(default, out Vector2 uv0, out Vector2 uv1, out Vector2 uv2, out Vector2 uv3);

    AssertNear(new Vector2(0f, 0f), uv0);
    AssertNear(new Vector2(1f, 0f), uv1);
    AssertNear(new Vector2(0f, 1f), uv2);
    AssertNear(new Vector2(1f, 1f), uv3);
}

static void TextureUv_SourceRect_MapsCorners()
{
    TextureUvTransform.GetTextureCoords(new Vector4(0.25f, 0.125f, 0.5f, 0.375f), out Vector2 uv0, out Vector2 uv1, out Vector2 uv2, out Vector2 uv3);

    AssertNear(new Vector2(0.25f, 0.125f), uv0);
    AssertNear(new Vector2(0.5f, 0.125f), uv1);
    AssertNear(new Vector2(0.25f, 0.375f), uv2);
    AssertNear(new Vector2(0.5f, 0.375f), uv3);
}

static void CommandBuffer_AddRectCentered_ConvertsCenterToPositionAndOrigin()
{
    FakeCommandBuffer buffer = new FakeCommandBuffer();

    buffer.AddRectCentered(
        new Vector2(100f, 50f),
        new Vector2(20f, 10f),
        Color.Red,
        rotationRadians: 1.25f,
        space: DrawSpace.World);

    if (buffer.RectCount != 1)
    {
        throw new InvalidOperationException($"Expected 1 rect command, actual {buffer.RectCount}.");
    }

    DrawRectCmd cmd = buffer.Rect;
    AssertNear(new Vector2(90f, 45f), new Vector2(cmd.Rectangle.X, cmd.Rectangle.Y));
    AssertNear(new Vector2(20f, 10f), new Vector2(cmd.Rectangle.Width, cmd.Rectangle.Height));
    AssertNear(new Vector2(10f, 5f), cmd.Origin);
    AssertEqual(1.25f, cmd.RotationRadians);
    AssertEqual(DrawSpace.World, cmd.Space);
}

static void CommandBuffer_AddTextureCentered_PreservesTransformSpaceAndUv()
{
    FakeCommandBuffer buffer = new FakeCommandBuffer();
    FakeTexture texture = new FakeTexture();
    Vector4 sourceUv = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);

    buffer.AddTextureCentered(
        texture,
        new Vector2(50f, 60f),
        new Vector2(8f, 12f),
        Color.White,
        rotationRadians: 0.75f,
        space: DrawSpace.World,
        sourceUv: sourceUv);

    if (buffer.TextureCount != 1)
    {
        throw new InvalidOperationException($"Expected 1 texture command, actual {buffer.TextureCount}.");
    }

    DrawTextureCmd cmd = buffer.Texture;
    AssertReferenceSame(texture, cmd.Texture);
    AssertNear(new Vector2(46f, 54f), cmd.Position);
    AssertNear(new Vector2(8f, 12f), cmd.Size);
    AssertNear(new Vector2(4f, 6f), cmd.Origin);
    AssertNear4(sourceUv, cmd.SourceUv);
    AssertEqual(0.75f, cmd.RotationRadians);
    AssertEqual(DrawSpace.World, cmd.Space);
}

static void CommandBuffer_AddTexture_DefaultsToTopLeftFullTextureScreenSpace()
{
    FakeCommandBuffer buffer = new FakeCommandBuffer();
    FakeTexture texture = new FakeTexture();

    buffer.AddTexture(
        texture,
        new Vector2(4f, 6f),
        new Vector2(16f, 24f),
        Color.White);

    DrawTextureCmd cmd = buffer.Texture;
    AssertReferenceSame(texture, cmd.Texture);
    AssertNear(new Vector2(4f, 6f), cmd.Position);
    AssertNear(new Vector2(16f, 24f), cmd.Size);
    AssertNear(Vector2.Zero, cmd.Origin);
    AssertNear4(Vector4.Zero, cmd.SourceUv);
    AssertEqual(0f, cmd.RotationRadians);
    AssertEqual(DrawSpace.Screen, cmd.Space);
}

static void CommandBuffer_AddText_DefaultsToTopLeftScreenSpace()
{
    FakeCommandBuffer buffer = new FakeCommandBuffer();
    AtlasFont font = CreateTestFont(new FakeTexture());

    buffer.AddText(
        font,
        "Text",
        new Vector2(12f, 24f),
        18f,
        Color.Yellow);

    if (buffer.TextCount != 1)
    {
        throw new InvalidOperationException($"Expected 1 text command, actual {buffer.TextCount}.");
    }

    DrawTextCmd cmd = buffer.Text;
    AssertReferenceSame(font, cmd.Font);
    AssertText("Text", cmd.Text);
    AssertNear(new Vector2(12f, 24f), cmd.Position);
    AssertNear(Vector2.Zero, cmd.Origin);
    AssertEqual(18f, cmd.Size);
    AssertEqual(0f, cmd.RotationRadians);
    AssertEqual(DrawSpace.Screen, cmd.Space);
}

static void CommandBuffer_AddTextCentered_UsesMeasuredSizeAsOrigin()
{
    FakeCommandBuffer buffer = new FakeCommandBuffer();
    AtlasFont font = CreateTestFont(new FakeTexture());

    buffer.AddTextCentered(
        font,
        "AB",
        new Vector2(100f, 80f),
        new Vector2(34f, 36f),
        32f,
        Color.White,
        rotationRadians: 0.5f,
        space: DrawSpace.World);

    DrawTextCmd cmd = buffer.Text;
    AssertReferenceSame(font, cmd.Font);
    AssertText("AB", cmd.Text);
    AssertNear(new Vector2(83f, 62f), cmd.Position);
    AssertNear(new Vector2(17f, 18f), cmd.Origin);
    AssertEqual(32f, cmd.Size);
    AssertEqual(0.5f, cmd.RotationRadians);
    AssertEqual(DrawSpace.World, cmd.Space);
}

static void FontAtlasParser_Parse_NormalizesGlyphUvAndMetrics()
{
    const string json = """
                        {
                          "atlas": "Fonts/test.png",
                          "size": 16,
                          "lineHeight": 18,
                          "ascender": 14,
                          "descender": -4,
                          "distanceRange": 4,
                          "atlasWidth": 128,
                          "atlasHeight": 64,
                          "glyphs": [
                            {
                              "codepoint": 65,
                              "x": 16,
                              "y": 8,
                              "width": 32,
                              "height": 16,
                              "bearingX": 1,
                              "bearingY": 12,
                              "advance": 10
                            }
                          ]
                        }
                        """;

    byte[] data = Encoding.UTF8.GetBytes(json);
    FontAtlasData fontData = FontAtlasParser.Parse(data);

    AssertEqual("Fonts/test.png", fontData.AtlasPath);
    AssertEqual(16f, fontData.Metrics.Size);
    AssertEqual(18f, fontData.Metrics.LineHeight);
    AssertEqual(14f, fontData.Metrics.Ascender);
    AssertEqual(-4f, fontData.Metrics.Descender);
    AssertEqual(4f, fontData.Metrics.DistanceRange);
    AssertEqual(1, fontData.Glyphs.Length);

    FontGlyph glyph = fontData.Glyphs[0];
    AssertEqual((uint)'A', glyph.Codepoint);
    AssertNear(new Vector2(32f, 16f), glyph.Size);
    AssertNear(new Vector2(1f, 12f), glyph.Bearing);
    AssertEqual(10f, glyph.Advance);
    AssertNear4(new Vector4(0.125f, 0.125f, 0.375f, 0.375f), glyph.SourceUv);
}

static void AtlasFont_TryGetGlyph_FindsExistingAndRejectsMissing()
{
    FakeTexture texture = new FakeTexture();
    FontGlyph glyphA = new FontGlyph(
        (uint)'A',
        new Vector2(8f, 10f),
        new Vector2(1f, 9f),
        9f,
        new Vector4(0f, 0f, 0.25f, 0.5f));
    FontAtlasMetrics metrics = new FontAtlasMetrics(16f, 18f, 14f, -4f, 4f);
    AtlasFont font = new AtlasFont(texture, in metrics, [glyphA]);

    if (!font.TryGetGlyph((uint)'A', out FontGlyph found))
    {
        throw new InvalidOperationException("Expected glyph 'A' to be found.");
    }

    if (font.TryGetGlyph((uint)'B', out _))
    {
        throw new InvalidOperationException("Expected glyph 'B' to be missing.");
    }

    AssertEqual((uint)'A', found.Codepoint);
    AssertNear(new Vector2(8f, 10f), found.Size);
    AssertNear(new Vector2(1f, 9f), found.Bearing);
    AssertEqual(9f, found.Advance);
    AssertNear4(new Vector4(0f, 0f, 0.25f, 0.5f), found.SourceUv);
    AssertReferenceSame(texture, font.AtlasTexture);
    AssertEqual(16f, font.Size);
    AssertEqual(18f, font.LineHeight);

    font.Dispose();
    if (!texture.IsDisposed)
    {
        throw new InvalidOperationException("Expected font dispose to dispose atlas texture.");
    }
}

static void AtlasFont_Dispose_RespectsAtlasTextureOwnership()
{
    FakeTexture ownedTexture = new FakeTexture();
    FontAtlasMetrics metrics = new FontAtlasMetrics(16f, 18f, 14f, -4f, 4f);
    AtlasFont ownedFont = new AtlasFont(ownedTexture, in metrics, []);

    ownedFont.Dispose();
    if (!ownedTexture.IsDisposed)
    {
        throw new InvalidOperationException("Expected owned atlas texture to be disposed with the font.");
    }

    FakeTexture externalTexture = new FakeTexture();
    AtlasFont externalFont = new AtlasFont(externalTexture, in metrics, [], ownsAtlasTexture: false);

    externalFont.Dispose();
    if (externalTexture.IsDisposed)
    {
        throw new InvalidOperationException("Expected external atlas texture to stay alive after font dispose.");
    }
}

static void TextLayout_Build_EmitsGlyphQuadsAndSize()
{
    AtlasFont font = CreateTestFont(new FakeTexture());
    Span<TextGlyphQuad> quads = stackalloc TextGlyphQuad[2];

    TextLayoutResult result = TextLayout.Build(font, "AB", 32f, quads);

    AssertEqual(2, result.GlyphCount);
    AssertEqual(false, result.IsTruncated);
    AssertNear(new Vector2(0f, 10f), quads[0].Position);
    AssertNear(new Vector2(16f, 20f), quads[0].Size);
    AssertNear4(new Vector4(0f, 0f, 0.25f, 0.5f), quads[0].SourceUv);
    AssertNear(new Vector2(18f, 12f), quads[1].Position);
    AssertNear(new Vector2(12f, 18f), quads[1].Size);
    AssertNear(new Vector2(34f, 36f), result.Size);
}

static void TextLayout_Build_HandlesNewlinesMissingGlyphsAndTruncation()
{
    AtlasFont font = CreateTestFont(new FakeTexture());
    Span<TextGlyphQuad> quads = stackalloc TextGlyphQuad[1];

    TextLayoutResult result = TextLayout.Build(font, "A?\nB", 16f, quads);

    AssertEqual(1, result.GlyphCount);
    AssertEqual(true, result.IsTruncated);
    AssertNear(new Vector2(0f, 5f), quads[0].Position);
    AssertNear(new Vector2(9f, 36f), result.Size);
}

static AtlasFont CreateTestFont(ITexture2D texture)
{
    FontAtlasMetrics metrics = new FontAtlasMetrics(16f, 18f, 14f, -4f, 4f);
    FontGlyph glyphA = new FontGlyph(
        (uint)'A',
        new Vector2(8f, 10f),
        new Vector2(0f, 9f),
        9f,
        new Vector4(0f, 0f, 0.25f, 0.5f));
    FontGlyph glyphB = new FontGlyph(
        (uint)'B',
        new Vector2(6f, 9f),
        new Vector2(0f, 8f),
        8f,
        new Vector4(0.25f, 0f, 0.5f, 0.5f));
    return new AtlasFont(texture, in metrics, [glyphA, glyphB]);
}

static Vector2 ToClip(Vector2 point, float width, float height)
{
    return new Vector2(
        (point.X / width) * 2f - 1f,
        1f - (point.Y / height) * 2f);
}

static void AssertNear(Vector2 expected, Vector2 actual, float tolerance = 0.0001f)
{
    if (MathF.Abs(expected.X - actual.X) > tolerance || MathF.Abs(expected.Y - actual.Y) > tolerance)
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }
}

static void AssertNear4(Vector4 expected, Vector4 actual, float tolerance = 0.0001f)
{
    if (MathF.Abs(expected.X - actual.X) > tolerance ||
        MathF.Abs(expected.Y - actual.Y) > tolerance ||
        MathF.Abs(expected.Z - actual.Z) > tolerance ||
        MathF.Abs(expected.W - actual.W) > tolerance)
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }
}

static void AssertReferenceSame(object expected, object actual)
{
    if (!ReferenceEquals(expected, actual))
    {
        throw new InvalidOperationException("Expected references to be the same instance.");
    }
}

static void AssertText(ReadOnlySpan<char> expected, ReadOnlyMemory<char> actual)
{
    if (!expected.SequenceEqual(actual.Span))
    {
        throw new InvalidOperationException($"Expected text '{expected.ToString()}', actual '{actual.ToString()}'.");
    }
}

internal sealed class FakeCommandBuffer : ICommandBuffer
{
    public int FrameId => 0;
    public DrawRectCmd Rect;
    public DrawTextureCmd Texture;
    public DrawTextCmd Text;
    public int RectCount;
    public int TextureCount;
    public int TextCount;

    public void Add(in DrawRectCmd cmd)
    {
        Rect = cmd;
        RectCount++;
    }

    public void Add(in DrawTextureCmd cmd)
    {
        Texture = cmd;
        TextureCount++;
    }

    public void Add(in DrawTextCmd cmd)
    {
        Text = cmd;
        TextCount++;
    }

    public ReadOnlySpan<DrawRectCmd> GetRectCommands()
    {
        return ReadOnlySpan<DrawRectCmd>.Empty;
    }

    public ReadOnlySpan<DrawTextureCmd> GetTextureCommands()
    {
        return ReadOnlySpan<DrawTextureCmd>.Empty;
    }

    public ReadOnlySpan<DrawTextCmd> GetTextCommands()
    {
        return ReadOnlySpan<DrawTextCmd>.Empty;
    }

    void ICommandBuffer.Clear()
    {
    }
}

internal sealed class FakeTexture : ITexture2D
{
    public bool IsDisposed;

    public void Dispose()
    {
        IsDisposed = true;
    }
}
