using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class LayoutBox
{
    public Rectangle MarginRect { get; set; }
    public Rectangle BorderRect { get; set; }
    public Rectangle PaddingRect { get; set; }
    public Rectangle ContentRect { get; set; }
    
    public void SetX(float newX)
    {
        float deltaX = newX - MarginRect.X;

        if (Math.Abs(deltaX) < 0.001f) return;

        MarginRect  = new Rectangle(MarginRect.X + deltaX, MarginRect.Y, MarginRect.Width, MarginRect.Height);
        BorderRect  = new Rectangle(BorderRect.X + deltaX, BorderRect.Y, BorderRect.Width, BorderRect.Height);
        PaddingRect = new Rectangle(PaddingRect.X + deltaX, PaddingRect.Y, PaddingRect.Width, PaddingRect.Height);
        ContentRect = new Rectangle(ContentRect.X + deltaX, ContentRect.Y, ContentRect.Width, ContentRect.Height);
    }
    
    public void SetY(float newY)
    {
        float deltaY = newY - MarginRect.Y;

        if (Math.Abs(deltaY) < 0.001f) return;

        MarginRect  = new Rectangle(MarginRect.X, MarginRect.Y + deltaY, MarginRect.Width, MarginRect.Height);
        BorderRect  = new Rectangle(BorderRect.X, BorderRect.Y + deltaY, BorderRect.Width, BorderRect.Height);
        PaddingRect = new Rectangle(PaddingRect.X, PaddingRect.Y + deltaY, PaddingRect.Width, PaddingRect.Height);
        ContentRect = new Rectangle(ContentRect.X, ContentRect.Y + deltaY, ContentRect.Width, ContentRect.Height);
    }
}