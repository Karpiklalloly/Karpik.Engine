using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class UIManager
{
    public UIElement Root { get; private set; }
    public Font Font { get; set; }
    
    private StyleComputer _styleComputer;
    private LayoutEngine _layoutEngine;
    private Renderer _renderer;
    
    public void SetRoot(UIElement element)
    {
        Root = element;
        _styleComputer = new StyleComputer();
        _layoutEngine = new LayoutEngine();
        _renderer = new Renderer();
    }

    public void Update(double dt)
    {
        _styleComputer.ComputeStyles(Root, StyleSheet.Default);
        Rectangle viewport = new Rectangle(0, 0, Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
        _layoutEngine.Layout(Root, viewport);
    }

    public void Render(double dt)
    {
        _renderer.Render(Root);
    }
}