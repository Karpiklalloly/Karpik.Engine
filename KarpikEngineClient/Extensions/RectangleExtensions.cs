using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client;

public static class RectangleExtensions
{
    public static bool Contains(this Rectangle rect, Vector2 point)
    {
        return point.X > rect.X
               && point.Y > rect.Y
               && point.X < rect.X + rect.Width
               && point.Y < rect.Y + rect.Height;
    }
}