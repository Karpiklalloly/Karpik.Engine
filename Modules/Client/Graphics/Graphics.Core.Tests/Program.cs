using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;

var tests = new (string Name, Action Run)[]
{
    ("Camera_WorldToScreen_MapsCameraPositionToViewportCenter", Camera_WorldToScreen_MapsCameraPositionToViewportCenter),
    ("Camera_ScreenToWorld_RoundTripsWorldPoint", Camera_ScreenToWorld_RoundTripsWorldPoint),
    ("Camera_Normalized_KeepsPositionWhenViewportFallbackIsUsed", Camera_Normalized_KeepsPositionWhenViewportFallbackIsUsed),
    ("Quad_ScreenZeroRotation_MapsPixelsToClipSpace", Quad_ScreenZeroRotation_MapsPixelsToClipSpace),
    ("Quad_ScreenCenterRotation_RotatesAroundOrigin", Quad_ScreenCenterRotation_RotatesAroundOrigin),
    ("Quad_WorldSpace_UsesCameraPositionAndScale", Quad_WorldSpace_UsesCameraPositionAndScale),
    ("Quad_ScreenSpace_IgnoresCamera", Quad_ScreenSpace_IgnoresCamera),
    ("TextureUv_Default_UsesFullTexture", TextureUv_Default_UsesFullTexture),
    ("TextureUv_SourceRect_MapsCorners", TextureUv_SourceRect_MapsCorners)
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
