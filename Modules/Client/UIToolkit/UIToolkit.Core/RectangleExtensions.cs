using System.Drawing;
using System.Numerics;

namespace Karpik.Engine.Client.UIToolkit;

public static class RectangleExtensions
{
    extension(RectangleF rectangle)
    {
        public bool Contains(Vector2 point)
        {
            return rectangle.Contains(point.X, point.Y);
        }
    }
}