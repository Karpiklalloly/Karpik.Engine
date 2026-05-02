using System.Drawing;
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;

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
    ("AtlasFont_TryGetGlyph_FindsExistingAndRejectsMissing", AtlasFont_TryGetGlyph_FindsExistingAndRejectsMissing)
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

internal sealed class FakeCommandBuffer : ICommandBuffer
{
    public int FrameId => 0;
    public DrawRectCmd Rect;
    public DrawTextureCmd Texture;
    public int RectCount;
    public int TextureCount;

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
