using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.VisualElements;

public static class Utils
{
    public static Texture2D _pixelTexture;
    public static Texture2D GetPixelTexture()
    {
        if (!Raylib.IsTextureValid(_pixelTexture))
        {
            var image = Raylib.GenImageColor(1, 1, Color.White);
            var texture = Raylib.LoadTextureFromImage(image);
            _pixelTexture = texture;
            Raylib.UnloadImage(image);
        }
        return _pixelTexture;
    }

    public static Vector2 GetTextPosition(Rectangle bounds, Vector2 size, Vector2 textAlignment)
    {
        return new Vector2(
            bounds.X + (bounds.Width - size.X) * textAlignment.X,
            bounds.Y + (bounds.Height - size.Y) * textAlignment.Y
        );
    }

    public static void DrawText(Font font, string text, Vector2 position, float fontSize, Color color)
    {
        if (string.IsNullOrEmpty(text) || !Raylib.IsFontValid(font)) return;
        
        Raylib.DrawTextEx(font, text, position, fontSize, 0, color);
    }

    public static void DrawRectangle(Rectangle rectangle, Color color)
    {
        Raylib.DrawRectangle((int)rectangle.X, (int)rectangle.Y, (int)rectangle.Width, (int)rectangle.Height, color);
    }
}