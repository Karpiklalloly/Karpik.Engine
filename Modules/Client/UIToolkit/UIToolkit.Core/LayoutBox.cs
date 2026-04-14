using System.Drawing;
using Veldrid;

namespace Karpik.Engine.Client.UIToolkit;

public class LayoutBox
{
    public RectangleF MarginRect { get; set; }
    public RectangleF BorderRect { get; set; }
    public RectangleF PaddingRect { get; set; }
    public RectangleF ContentRect { get; set; }
    
    public void SetX(float newX)
    {
        float deltaX = newX - MarginRect.X;

        if (Math.Abs(deltaX) < 0.001f) return;

        MarginRect  = new RectangleF(MarginRect.X + deltaX, MarginRect.Y, MarginRect.Width, MarginRect.Height);
        BorderRect  = new RectangleF(BorderRect.X + deltaX, BorderRect.Y, BorderRect.Width, BorderRect.Height);
        PaddingRect = new RectangleF(PaddingRect.X + deltaX, PaddingRect.Y, PaddingRect.Width, PaddingRect.Height);
        ContentRect = new RectangleF(ContentRect.X + deltaX, ContentRect.Y, ContentRect.Width, ContentRect.Height);
    }
    
    public void SetY(float newY)
    {
        float deltaY = newY - MarginRect.Y;

        if (Math.Abs(deltaY) < 0.001f) return;

        MarginRect  = new RectangleF(MarginRect.X, MarginRect.Y + deltaY, MarginRect.Width, MarginRect.Height);
        BorderRect  = new RectangleF(BorderRect.X, BorderRect.Y + deltaY, BorderRect.Width, BorderRect.Height);
        PaddingRect = new RectangleF(PaddingRect.X, PaddingRect.Y + deltaY, PaddingRect.Width, PaddingRect.Height);
        ContentRect = new RectangleF(ContentRect.X, ContentRect.Y + deltaY, ContentRect.Width, ContentRect.Height);
    }
}