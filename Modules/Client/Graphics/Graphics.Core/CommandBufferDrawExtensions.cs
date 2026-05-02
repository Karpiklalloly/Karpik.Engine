using System.Drawing;
using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public static class CommandBufferDrawExtensions
{
    public static void AddRect(
        this ICommandBuffer buffer,
        RectangleF rectangle,
        Color color,
        Vector2 origin = default,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen)
    {
        DrawRectCmd cmd = new DrawRectCmd
        {
            Rectangle = rectangle,
            Color = color,
            Origin = origin,
            RotationRadians = rotationRadians,
            Space = space
        };

        buffer.Add(in cmd);
    }

    public static void AddRectCentered(
        this ICommandBuffer buffer,
        Vector2 center,
        Vector2 size,
        Color color,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen)
    {
        Vector2 position = center - size * 0.5f;
        DrawRectCmd cmd = new DrawRectCmd
        {
            Rectangle = new RectangleF(position.X, position.Y, size.X, size.Y),
            Color = color,
            Origin = size * 0.5f,
            RotationRadians = rotationRadians,
            Space = space
        };

        buffer.Add(in cmd);
    }

    public static void AddTexture(
        this ICommandBuffer buffer,
        ITexture2D texture,
        Vector2 position,
        Vector2 size,
        Color color,
        Vector2 origin = default,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen,
        Vector4 sourceUv = default)
    {
        DrawTextureCmd cmd = new DrawTextureCmd
        {
            Texture = texture,
            Position = position,
            Size = size,
            Color = color,
            Origin = origin,
            SourceUv = sourceUv,
            RotationRadians = rotationRadians,
            Space = space
        };

        buffer.Add(in cmd);
    }

    public static void AddTextureCentered(
        this ICommandBuffer buffer,
        ITexture2D texture,
        Vector2 center,
        Vector2 size,
        Color color,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen,
        Vector4 sourceUv = default)
    {
        Vector2 position = center - size * 0.5f;
        DrawTextureCmd cmd = new DrawTextureCmd
        {
            Texture = texture,
            Position = position,
            Size = size,
            Color = color,
            Origin = size * 0.5f,
            SourceUv = sourceUv,
            RotationRadians = rotationRadians,
            Space = space
        };

        buffer.Add(in cmd);
    }
}
