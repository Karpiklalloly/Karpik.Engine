using System.Drawing;
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Modules.Window.Core;
using s = Karpik.Engine.Client.UIToolkit.StyleSheet;

namespace Karpik.Engine.Client.UIToolkit;

public class UIManager
{
    public UIElement Root { get; private set; }
    public IFont Font { get; set; }
    
    private StyleComputer _styleComputer = null!;
    private LayoutEngine _layoutEngine = null!;
    private Renderer _renderer = null!;
    
    private UIElement? _hoveredElement;
    private UIElement? _pressedElement;
    
    private bool _isLayoutDirtyThisFrame;

    private IRenderTexture2D _renderTexture = null!;
    private Input _input = null!;
    private IRenderer2D _gRenderer = null!;
    private IWindow _window = null!;

    public void SetRoot(UIElement element, Input input, IRenderer2D renderer, IWindow window)
    {
        _gRenderer = renderer;
        _window = window;
        _input = input;
        Root = element;
        _styleComputer = new StyleComputer();
        _layoutEngine = new LayoutEngine();
        _layoutEngine.Init(renderer);
        _renderer = new Renderer(renderer);
        var rectangle = _gRenderer.GetScreenRectangle();
        _renderTexture = _gRenderer.LoadRenderTexture((int)rectangle.Width, (int)rectangle.Height);
    }

    public void Update()
    {
        _isLayoutDirtyThisFrame = false;
        
        HandleInteractivity();
        
        ProcessStyles(Root, null, s.Default);

        if (_isLayoutDirtyThisFrame)
        {
            _layoutEngine.Layout(Root, _gRenderer.GetScreenRectangle(), Font);
            
            // Очищаем флаги компоновки после ее завершения
            ClearLayoutFlags(Root);
        }
    }

    public void Render()
    {
        if (_window.IsResized)
        {
            _gRenderer.UnloadTexture(_renderTexture.Texture);
            _gRenderer.UnloadRenderTexture(_renderTexture);
            _renderTexture = _gRenderer.LoadRenderTexture(_window.Width, _window.Height);
        }
        
        _gRenderer.BeginTextureMode(_renderTexture);
        _gRenderer.ClearBackground(Color.FromArgb(0, 0, 0, 0));
        _renderer.Render(Root, Font);
        _gRenderer.EndTextureMode();
        _gRenderer.DrawTexture(_renderTexture.Texture, new RectangleF(0, 0, 
            _renderTexture.Texture.Width, -_renderTexture.Texture.Height),
            Vector2.Zero, Color.White);
    }
    
    private void ProcessStyles(UIElement element, Dictionary<string, string>? parentComputedStyle, StyleSheet styleSheet)
    {
        if (element.Dirty.HasFlag(DirtyFlag.Style))
        {
            _styleComputer.ComputeStylesForNode(element, styleSheet, parentComputedStyle);
            element.ClearDirtyFlag(DirtyFlag.Style);
            _isLayoutDirtyThisFrame = true;
        }

        foreach (var child in element.Children)
        {
            ProcessStyles(child, element.ComputedStyle, styleSheet);
        }
    }
    
    private void ClearLayoutFlags(UIElement element)
    {
        element.ClearDirtyFlag(DirtyFlag.Layout);
        foreach (var child in element.Children)
        {
            ClearLayoutFlags(child);
        }
    }
    
    private void HandleInteractivity()
    {
        var mousePos = _input.MousePosition;
        var currentHover = HitTest(Root, mousePos);
        
        if (currentHover != _hoveredElement)
        {
            if (_hoveredElement != null)
            {
                _hoveredElement.IsHovered = false;
                _hoveredElement.MarkDirty(DirtyFlag.Style);
                foreach (var m in _hoveredElement.Manipulators) m.OnMouseLeave();
            }
            if (currentHover != null)
            {
                currentHover.IsHovered = true;
                currentHover.MarkDirty(DirtyFlag.Style);
                foreach (var m in currentHover.Manipulators) m.OnMouseEnter();
            }
            _hoveredElement = currentHover;
        }
        
        if (_input.IsMouseLeftButtonDown)
        {
            if (_hoveredElement != null && _pressedElement == null)
            {
                _pressedElement = _hoveredElement;
                _pressedElement.IsActive = true;
                _pressedElement.MarkDirty(DirtyFlag.Style);
                foreach (var m in _pressedElement.Manipulators) m.OnMouseDown();
            }
        }
        
        if (_input.IsMouseLeftButtonUp)
        {
            if (_pressedElement != null)
            {
                _pressedElement.IsActive = false;
                _pressedElement.MarkDirty(DirtyFlag.Style);
                
                if (_hoveredElement != null)
                {
                    foreach (var m in _hoveredElement.Manipulators) m.OnMouseUp();
                }

                if (_pressedElement == _hoveredElement && _hoveredElement != null)
                {
                    foreach (var m in _pressedElement.Manipulators) m.OnClick();
                }
                
                _pressedElement = null;
            }
        }
    }
    
    private UIElement? HitTest(UIElement element, Vector2 point)
    {
        if (element.ComputedStyle.GetValueOrDefault("display") == "none")
        {
            return null;
        }

        var childrenToCheck = element.Children.ToList()
            .OrderBy(static c => c.GetPosition() == "static" ? 0 : 1) // Сначала непозиционированные
            .ThenBy(static c => GetZIndex(c));

        foreach (var child in childrenToCheck.Reverse<UIElement>())
        {
            var hit = HitTest(child, point);
            if (hit != null)
            {
                return hit;
            }
        }

        if (element.LayoutBox.BorderRect.Contains(point))
        {
            return element;
        }

        return null;
    }

    private static int GetZIndex(UIElement element)
    {
        if (element.ComputedStyle.TryGetValue(s.z_index, out var zIndexStr))
        {
            if (int.TryParse(zIndexStr, out int zIndex))
            {
                return zIndex;
            }
        }
        return 0;
    }
}