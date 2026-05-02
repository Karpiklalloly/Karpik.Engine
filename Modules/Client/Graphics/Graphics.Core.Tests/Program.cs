using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;

var tests = new (string Name, Action Run)[]
{
    ("Camera_WorldToScreen_MapsCameraPositionToViewportCenter", Camera_WorldToScreen_MapsCameraPositionToViewportCenter),
    ("Camera_ScreenToWorld_RoundTripsWorldPoint", Camera_ScreenToWorld_RoundTripsWorldPoint),
    ("Quad_ScreenZeroRotation_MapsPixelsToClipSpace", Quad_ScreenZeroRotation_MapsPixelsToClipSpace),
    ("Quad_ScreenCenterRotation_RotatesAroundOrigin", Quad_ScreenCenterRotation_RotatesAroundOrigin),
    ("Quad_WorldSpace_UsesCameraPositionAndScale", Quad_WorldSpace_UsesCameraPositionAndScale)
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
