

using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.VisualElements;

public class Panel : VisualElement
{
    public Color Color { get; set; } = new  Color(255, 255, 255, 0);
    public Texture2D Texture { get; set; } //TODO: Add Texture draw
    
    public Panel(Vector2 size) : base(size) { }
    public Panel(Vector2 size, Color color) : base(size)
    {
        Color = color;
    }

    protected override void DrawSelf()
    {
        if (Color.A > 0)
        {
            Utils.DrawRectangle(Bounds, Color);
        }
        // if (Raylib.IsTextureValid(BackgroundTexture))
        // {
        //     Ra
        //     spriteBatch.Draw(BackgroundTexture, Bounds, Color.White);
        // }
    }
}