using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

internal static class QuadTransform2D
{
    public static void BuildScreenQuad(
        in DrawTransform2D transform,
        float framebufferWidth,
        float framebufferHeight,
        out Vector2 p0,
        out Vector2 p1,
        out Vector2 p2,
        out Vector2 p3)
    {
        if (transform.RotationRadians == 0f)
        {
            float l = (transform.Position.X / framebufferWidth) * 2f - 1f;
            float r = ((transform.Position.X + transform.Size.X) / framebufferWidth) * 2f - 1f;
            float t = 1f - (transform.Position.Y / framebufferHeight) * 2f;
            float b = 1f - ((transform.Position.Y + transform.Size.Y) / framebufferHeight) * 2f;

            p0 = new Vector2(l, t);
            p1 = new Vector2(r, t);
            p2 = new Vector2(l, b);
            p3 = new Vector2(r, b);
            return;
        }

        float sin = MathF.Sin(transform.RotationRadians);
        float cos = MathF.Cos(transform.RotationRadians);
        Vector2 pivot = transform.Position + transform.Origin;

        p0 = ToClip(RotatePoint(transform.Position, pivot, sin, cos), framebufferWidth, framebufferHeight);
        p1 = ToClip(RotatePoint(transform.Position + new Vector2(transform.Size.X, 0f), pivot, sin, cos), framebufferWidth, framebufferHeight);
        p2 = ToClip(RotatePoint(transform.Position + new Vector2(0f, transform.Size.Y), pivot, sin, cos), framebufferWidth, framebufferHeight);
        p3 = ToClip(RotatePoint(transform.Position + transform.Size, pivot, sin, cos), framebufferWidth, framebufferHeight);
    }

    private static Vector2 RotatePoint(Vector2 point, Vector2 pivot, float sin, float cos)
    {
        Vector2 local = point - pivot;
        return new Vector2(
            pivot.X + local.X * cos - local.Y * sin,
            pivot.Y + local.X * sin + local.Y * cos);
    }

    private static Vector2 ToClip(Vector2 point, float framebufferWidth, float framebufferHeight)
    {
        return new Vector2(
            (point.X / framebufferWidth) * 2f - 1f,
            1f - (point.Y / framebufferHeight) * 2f);
    }
}
