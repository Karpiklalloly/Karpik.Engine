using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public struct Camera2D
{
    public Vector2 Position;
    public float RotationRadians;
    public float Zoom;
    public Vector2 ViewportPosition;
    public Vector2 ViewportSize;
    public float PixelsPerUnit;

    public static Camera2D CreateDefault()
    {
        return CreateDefault(0f, 0f);
    }

    public static Camera2D CreateDefault(float viewportWidth, float viewportHeight)
    {
        return new Camera2D
        {
            Position = default,
            RotationRadians = 0f,
            Zoom = 1f,
            ViewportPosition = default,
            ViewportSize = new Vector2(viewportWidth, viewportHeight),
            PixelsPerUnit = 1f
        };
    }

    public readonly Camera2D WithViewport(float viewportWidth, float viewportHeight)
    {
        Camera2D camera = this;
        camera.ViewportPosition = default;
        camera.ViewportSize = new Vector2(viewportWidth, viewportHeight);
        return camera;
    }

    public readonly Camera2D Normalized(float fallbackViewportWidth, float fallbackViewportHeight)
    {
        Camera2D camera = this;
        if (camera.Zoom <= 0f)
        {
            camera.Zoom = 1f;
        }

        if (camera.PixelsPerUnit <= 0f)
        {
            camera.PixelsPerUnit = 1f;
        }

        if (camera.ViewportSize.X <= 0f || camera.ViewportSize.Y <= 0f)
        {
            camera.ViewportPosition = default;
            camera.ViewportSize = new Vector2(fallbackViewportWidth, fallbackViewportHeight);
        }

        return camera;
    }

    public readonly Vector2 WorldToScreen(Vector2 worldPosition)
    {
        Vector2 local = worldPosition - Position;
        if (RotationRadians != 0f)
        {
            float sin = MathF.Sin(-RotationRadians);
            float cos = MathF.Cos(-RotationRadians);
            local = new Vector2(
                local.X * cos - local.Y * sin,
                local.X * sin + local.Y * cos);
        }

        float scale = EffectiveScale();
        Vector2 viewportCenter = ViewportPosition + ViewportSize * 0.5f;
        return viewportCenter + local * scale;
    }

    public readonly Vector2 ScreenToWorld(Vector2 screenPosition)
    {
        float scale = EffectiveScale();
        Vector2 viewportCenter = ViewportPosition + ViewportSize * 0.5f;
        Vector2 local = (screenPosition - viewportCenter) / scale;

        if (RotationRadians != 0f)
        {
            float sin = MathF.Sin(RotationRadians);
            float cos = MathF.Cos(RotationRadians);
            local = new Vector2(
                local.X * cos - local.Y * sin,
                local.X * sin + local.Y * cos);
        }

        return Position + local;
    }

    private readonly float EffectiveScale()
    {
        float zoom = Zoom > 0f ? Zoom : 1f;
        float pixelsPerUnit = PixelsPerUnit > 0f ? PixelsPerUnit : 1f;
        return pixelsPerUnit * zoom;
    }
}
