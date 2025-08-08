using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class MouseEvent
{
    public Vector2 Position { get; set; }
    public MouseButton Button { get; set; }
    public bool Handled { get; set; } = false;
    
    public MouseEvent(Vector2 position, MouseButton button)
    {
        Position = position;
        Button = button;
    }
}