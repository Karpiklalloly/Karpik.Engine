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
                foreach (var m in _hoveredElement.Manipulators) m.OnMouseLeave();
            }
            if (currentHover != null)
            {
                currentHover.IsHovered = true;
                foreach (var m in currentHover.Manipulators) m.OnMouseEnter();
            }
            _hoveredElement = currentHover;
        }
        
        // --- ОБНОВЛЕННАЯ ЛОГИКА НАЖАТИЯ И КЛИКОВ ---

        // Обрабатываем НАЖАТИЕ мыши
        if (Input.IsMouseLeftButtonDown)
        {
            if (_hoveredElement != null)
            {
                // Запоминаем элемент, на котором началось нажатие
                _pressedElement = _hoveredElement;
                _pressedElement.IsActive = true;
                foreach (var m in _pressedElement.Manipulators) m.OnMouseDown();
            }
        }
        
        // Обрабатываем ОТПУСКАНИЕ мыши
        if (Input.IsMouseLeftButtonUp)
        {
            if (_pressedElement != null)
            {
                _pressedElement.IsActive = false; // Элемент больше не "активен"
                
                // Вызываем OnMouseUp для элемента, который был под курсором в момент отпускания
                if (_hoveredElement != null)
                {
                    foreach (var m in _hoveredElement.Manipulators) m.OnMouseUp();
                }

                // Если мышь отпущена над ТЕМ ЖЕ элементом, над которым была нажата - это КЛИК
                if (_pressedElement == _hoveredElement && _hoveredElement != null)
                {
                    foreach (var m in _pressedElement.Manipulators) m.OnClick();
                }

                // Сбрасываем состояние нажатия в любом случае
                _pressedElement = null;
            }
        }
    }
    
    // --- ФИНАЛЬНАЯ ВЕРСИЯ HITTEST: Рекурсивный обход с сортировкой по Z-Index ---
    private UIElement HitTest(UIElement element, Vector2 point)
    {
        if (element.ComputedStyle.GetValueOrDefault("display") == "none")
        {
            return null;
        }

        // 1. Создаем список дочерних элементов для проверки.
        var childrenToCheck = element.Children.ToList();
        
        // 2. Сортируем детей по z-index. Элементы с большим z-index должны проверяться первыми.
        childrenToCheck = childrenToCheck.OrderByDescending(c => GetZIndex(c)).ToList();

        // 3. Проверяем детей в отсортированном порядке.
        // Дети всегда проверяются ПЕРЕД родителем.
        foreach (var child in childrenToCheck)
        {
            var hit = HitTest(child, point);
            if (hit != null)
            {
                return hit; // Нашли в дочернем элементе, он выше
            }
        }

        // 4. Если не нашли в дочерних, проверяем сам элемент.
        if (Raylib.CheckCollisionPointRec(point, element.LayoutBox.BorderRect))
        {
            return element;
        }

        return null;
    }

    /// <summary>
    /// Вспомогательный метод для получения числового z-index из стиля.
    /// </summary>
    private int GetZIndex(UIElement element)
    {
        if (element.ComputedStyle.TryGetValue("z-index", out var zIndexStr))
        {
            if (int.TryParse(zIndexStr, out int zIndex))
            {
                return zIndex;
            }
        }
        return 0; // z-index по умолчанию
    }
}