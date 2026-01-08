using System.Drawing;

namespace Karpik.Engine.Client.Graphics.GRaylib;

public static class SystemToRaylibExtensions
{
    extension(Color color)
    {
        public Raylib_cs.Color Raylib => new(color.R, color.G, color.B, color.A);
    }
    
    extension(Raylib_cs.Color color)
    {
        public Color System => Color.FromArgb(color.A, color.R, color.G, color.B);
    }
    
    extension(RectangleF rectangle)
    {
        public Raylib_cs.Rectangle Raylib => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }
    
    extension(Raylib_cs.Rectangle rectangle)
    {
        public RectangleF System => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }
}