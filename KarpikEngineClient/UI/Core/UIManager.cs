using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class UIManager
{
    public UIElement Root { get; private set; }
    public Font Font { get; set; }
    
    private StyleComputer _styleComputer;
    private LayoutEngine _layoutEngine;
    private Renderer _renderer;
    
    private UIElement _hoveredElement;
    private UIElement _pressedElement;
    
    public void SetRoot(UIElement element)
    {
        Root = element;
        _styleComputer = new StyleComputer();
        _layoutEngine = new LayoutEngine();
        _renderer = new Renderer();
    }

    public void Update(double dt)
    {
        HandleInteractivity();
        
        _styleComputer.ComputeStyles(Root, StyleSheet.Default);
        Rectangle viewport = new Rectangle(0, 0, Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
        _layoutEngine.Layout(Root, viewport, Font);
    }

    public void Render(double dt)
    {
        _renderer.Render(Root, Font);
    }
    
    private void HandleInteractivity()
    {
        var mousePos = Input.MousePosition;
        var currentHover = HitTest(Root, mousePos);
        
        if (currentHover != _hoveredElement)
        {
            if (_hoveredElement != null)
            {
                _hoveredElement.IsHovered = false;
                foreach (var manipulator in _hoveredElement.Manipulators) manipulator.OnMouseLeave();
            }
            if (currentHover != null)
            {
                currentHover.IsHovered = true;
                foreach (var manipulator in currentHover.Manipulators) manipulator.OnMouseEnter();
            }
            _hoveredElement = currentHover;
        }

        // Обработка кликов
        if (Input.IsMouseLeftButtonDown)
        {
            if (_hoveredElement != null)
            {
                _pressedElement = _hoveredElement;
                foreach (var m in _hoveredElement.Manipulators) m.OnMouseDown();
            }
        }

        if (Input.IsMouseLeftButtonUp)
        {
            if (_hoveredElement != null)
            {
                foreach (var m in _hoveredElement.Manipulators) m.OnMouseUp();
                
                // Клик засчитывается, если мышь отпущена над тем же элементом, над которым была нажата
                if (_pressedElement == _hoveredElement)
                {
                    // Добавьте метод OnClick в Manipulator, чтобы это заработало
                    // foreach (var m in _hoveredElement.Manipulators) m.OnClick(); 
                }
            }
            _pressedElement = null;
        }
    }
    
    private UIElement HitTest(UIElement element, Vector2 point)
    {
        if (element.ComputedStyle.GetValueOrDefault("display") == "none") return null;
        
        for (int i = element.Children.Count - 1; i >= 0; i--)
        {
            var hit = HitTest(element.Children[i], point);
            if (hit != null) return hit;
        }
        
        if (Raylib.CheckCollisionPointRec(point, element.LayoutBox.BorderRect)) return element;
        
        return null;
    }
}