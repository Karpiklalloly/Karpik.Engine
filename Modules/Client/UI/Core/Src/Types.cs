using System;

namespace Karpik.Engine.Client.UI
{
    /// <summary>
    /// Simple rectangle struct for bounds.
    /// </summary>
    public struct Rectangle
    {
        public float X, Y, Width, Height;

        public Rectangle(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}