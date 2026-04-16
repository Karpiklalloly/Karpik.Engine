using System.Drawing;
using System.Numerics;

namespace Karpik.Engine.Client.Graphics.Core;

public enum DrawCommandType : byte
{
    None,
    Rect,
    Texture,
    Text
}

public struct DrawCommand
{
    public DrawCommandType Type;
    
    // Rectangle
    public RectangleF Rectangle;
    public Color RectangleColor;
    
    // Texture
    public ITexture2D Texture;
    public Vector2 TexturePosition;
    public Color TextureColor;
    
    // Text
    public IFont Font;
    public ReadOnlyMemory<char> Text;
    public Vector2 TextPosition;
    public float TextSize;
    public Color TextColor;
    
    public static DrawCommand FromRect(DrawRectCmd cmd) => new()
    {
        Type = DrawCommandType.Rect,
        Rectangle = cmd.Rectangle,
        RectangleColor = cmd.Color
    };

    public static DrawCommand FromTexture(DrawTextureCmd cmd) => new()
    {
        Type = DrawCommandType.Texture,
        Texture = cmd.Texture,
        TexturePosition = cmd.Position,
        TextureColor = cmd.Color
    };

    public static DrawCommand FromText(DrawTextCmd cmd) => new()
    {
        Type = DrawCommandType.Text,
        Font = cmd.Font,
        Text = cmd.Text,
        TextPosition = cmd.Position,
        TextSize = cmd.Size,
        TextColor = cmd.Color
    };
}