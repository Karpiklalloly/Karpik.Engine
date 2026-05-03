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

    public static void AddText(
        this ICommandBuffer buffer,
        IFont font,
        string text,
        Vector2 position,
        float size,
        Color color,
        Vector2 origin = default,
        TextAnchor anchor = TextAnchor.TopLeft,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen)
    {
        buffer.AddText(font, text.AsMemory(), position, size, color, origin, anchor, rotationRadians, space);
    }

    public static void AddText(
        this ICommandBuffer buffer,
        IFont font,
        ReadOnlyMemory<char> text,
        Vector2 position,
        float size,
        Color color,
        Vector2 origin = default,
        TextAnchor anchor = TextAnchor.TopLeft,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen)
    {
        DrawTextCmd cmd = new DrawTextCmd
        {
            Font = font,
            Text = text,
            Position = position,
            Origin = origin,
            Anchor = anchor,
            Size = size,
            RotationRadians = rotationRadians,
            Color = color,
            Space = space
        };

        buffer.Add(in cmd);
    }

    public static void AddTextCopy(
        this ICommandBuffer buffer,
        IFont font,
        ReadOnlySpan<char> text,
        Vector2 position,
        float size,
        Color color,
        Vector2 origin = default,
        TextAnchor anchor = TextAnchor.TopLeft,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen)
    {
        if (buffer is not ThreadBuffer threadBuffer)
        {
            throw new InvalidOperationException("AddTextCopy requires GraphicsContext.Buffer so text can be copied into the thread-local command buffer.");
        }

        buffer.AddText(
            font,
            threadBuffer.CopyText(text),
            position,
            size,
            color,
            origin,
            anchor,
            rotationRadians,
            space);
    }
    
    public static void AddTextCentered(
        this ICommandBuffer buffer,
        IFont font,
        string text,
        Vector2 center,
        float size,
        Color color,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen)
    {
        buffer.AddText(
            font,
            text.AsMemory(),
            center,
            size,
            color,
            origin: default,
            anchor: TextAnchor.Center,
            rotationRadians: rotationRadians,
            space: space);
    }

    public static void AddTextCenteredCopy(
        this ICommandBuffer buffer,
        IFont font,
        ReadOnlySpan<char> text,
        Vector2 center,
        float size,
        Color color,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen)
    {
        buffer.AddTextCopy(
            font,
            text,
            center,
            size,
            color,
            origin: default,
            anchor: TextAnchor.Center,
            rotationRadians: rotationRadians,
            space: space);
    }

    public static void AddTextCentered(
        this ICommandBuffer buffer,
        IFont font,
        string text,
        Vector2 center,
        Vector2 measuredSize,
        float size,
        Color color,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen)
    {
        buffer.AddTextCentered(font, text.AsMemory(), center, measuredSize, size, color, rotationRadians, space);
    }

    public static void AddTextCentered(
        this ICommandBuffer buffer,
        IFont font,
        ReadOnlyMemory<char> text,
        Vector2 center,
        Vector2 measuredSize,
        float size,
        Color color,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen)
    {
        Vector2 origin = measuredSize * 0.5f;
        DrawTextCmd cmd = new DrawTextCmd
        {
            Font = font,
            Text = text,
            Position = center - origin,
            Origin = origin,
            Anchor = TextAnchor.TopLeft,
            Size = size,
            RotationRadians = rotationRadians,
            Color = color,
            Space = space
        };

        buffer.Add(in cmd);
    }
}
