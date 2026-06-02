using Veldrid;

namespace Karpik.Engine.Modules.Window.Core;

public interface IWindow
{
    public event Action Resized;
    
    public int Width { get; }
    public int Height { get; }
    public string Title { get; set; }
    public WindowState WindowState { get; set; }
    public bool Exists { get; }
    public bool IsResized { get; }
}