using System.Drawing;
using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

[Karpik.Engine.Shared.ECS.Scheduling.RenderPrepareCommand]
public static class CommandBufferDrawExtensions
{
    public static void AddRect(
        this ICommandBuffer buffer,
        RectangleF rectangle,
        Color color,
        Vector2 origin = default,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen,
        ulong sortKey = 0)
    {
        DrawRectCmd cmd = new DrawRectCmd
        {
            Rectangle = rectangle,
            Color = color,
            Origin = origin,
            RotationRadians = rotationRadians,
            Space = space,
            SortKey = sortKey
        };

        buffer.Add(in cmd);
    }

    public static void AddRectCentered(
        this ICommandBuffer buffer,
        Vector2 center,
        Vector2 size,
        Color color,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen,
        ulong sortKey = 0)
    {
        Vector2 position = center - size * 0.5f;
        DrawRectCmd cmd = new DrawRectCmd
        {
            Rectangle = new RectangleF(position.X, position.Y, size.X, size.Y),
            Color = color,
            Origin = size * 0.5f,
            RotationRadians = rotationRadians,
            Space = space,
            SortKey = sortKey
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
        Vector4 sourceUv = default,
        ulong sortKey = 0)
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
            Space = space,
            SortKey = sortKey
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
        Vector4 sourceUv = default,
        ulong sortKey = 0)
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
            Space = space,
            SortKey = sortKey
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
        DrawSpace space = DrawSpace.Screen,
        ulong sortKey = 0)
    {
        buffer.AddText(font, text.AsMemory(), position, size, color, origin, anchor, rotationRadians, space, sortKey);
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
        DrawSpace space = DrawSpace.Screen,
        ulong sortKey = 0)
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
            Space = space,
            SortKey = sortKey
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
        DrawSpace space = DrawSpace.Screen,
        ulong sortKey = 0)
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
            space,
            sortKey);
    }
    
    public static void AddTextCentered(
        this ICommandBuffer buffer,
        IFont font,
        string text,
        Vector2 center,
        float size,
        Color color,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen,
        ulong sortKey = 0)
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
            space: space,
            sortKey: sortKey);
    }

    public static void AddTextCenteredCopy(
        this ICommandBuffer buffer,
        IFont font,
        ReadOnlySpan<char> text,
        Vector2 center,
        float size,
        Color color,
        float rotationRadians = 0f,
        DrawSpace space = DrawSpace.Screen,
        ulong sortKey = 0)
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
            space: space,
            sortKey: sortKey);
    }

}
