using System.Drawing;
using Karpik.Engine.Client.Graphics.Core;
using Raylib_cs;
using Color = System.Drawing.Color;

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

    extension(ICamera camera)
    {
        public Camera3D Raylib3D => ((RaylibCamera)camera).Camera;
    }
    
    extension(ICamera2D camera)
    {
        public Camera2D Raylib2D => ((RaylibCamera2D)camera).Raylib2D;
    }

    extension(ITexture2D texture2D)
    {
        public Texture2D Raylib => ((RaylibTexture2D)texture2D).Texture;
    }
}