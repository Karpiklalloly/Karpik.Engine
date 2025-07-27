using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.VisualElements;

public class Window : Panel
{
    public string Title { get; private set; }
    public Font TitleFont { get; set; }
    
    public Window(Vector2 size) : base(size)
    {
        
    }
}