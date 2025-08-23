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
    
    private bool _isLayoutDirtyThisFrame;
    
    public void SetRoot(UIElement element)
    {
        Root = element;
        _styleComputer = new StyleComputer();
        _layoutEngine = new LayoutEngine();
        _renderer = new Renderer();
    }

    public void Update(double dt)
    {
        _isLayoutDirtyThisFrame = false;
        
        HandleInteractivity();
        
        ProcessStyles(Root, null, StyleSheet.Default);

        if (_isLayoutDirtyThisFrame)
        {
            Rectangle viewport = new Rectangle(0, 0, Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
            _layoutEngine.Layout(Root, viewport, Font);
            
            // Очищаем флаги компоновки после ее завершения
            ClearLayoutFlags(Root);
        }
    }

    public void Render(double dt)
    {
        _renderer.Render(Root, Font);
    }
    
    private void ProcessStyles(UIElement element, Dictionary<string, string> parentComputedStyle, StyleSheet styleSheet)
    {
        // Если флаг установлен, пересчитываем стили для этого конкретного узла
        if (element.Dirty.HasFlag(DirtyFlag.Style))
        {
            // Вызываем публичный метод из StyleComputer, который делает всю работу для одного узла
            _styleComputer.ComputeStylesForNode(element, styleSheet, parentComputedStyle);

            // Очищаем флаг, так как работа выполнена
            element.ClearDirtyFlag(DirtyFlag.Style);
            
            // Любой пересчет стиля - это потенциальное изменение геометрии.
            // Взводим флаг, чтобы UIManager запустил LayoutEngine.
            _isLayoutDirtyThisFrame = true;
        }

        // Всегда продолжаем рекурсию, так как дочерний элемент может быть "грязным",
        // даже если родитель "чистый".
        foreach (var child in element.Children)
        {
            // Передаем дочерним элементам вычисленный стиль текущего элемента для наследования
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
        // Код из предыдущего ответа остается без изменений
        var mousePos = Input.MousePosition;
        var currentHover = HitTest(Root, mousePos);
        
        if (currentHover != _hoveredElement)
        {
            if (_hoveredElement != null)
            {
                _hoveredElement.IsHovered = false;
                _hoveredElement.MarkDirty(DirtyFlag.Style); // :hover состояние изменилось
                foreach (var m in _hoveredElement.Manipulators) m.OnMouseLeave();
            }
            if (currentHover != null)
            {
                currentHover.IsHovered = true;
                currentHover.MarkDirty(DirtyFlag.Style); // :hover состояние изменилось
                foreach (var m in currentHover.Manipulators) m.OnMouseEnter();
            }
            _hoveredElement = currentHover;
        }
        
        if (Input.IsMouseLeftButtonDown)
        {
            if (_hoveredElement != null && _pressedElement == null)
            {
                _pressedElement = _hoveredElement;
                _pressedElement.IsActive = true;
                _pressedElement.MarkDirty(DirtyFlag.Style); // :active состояние изменилось
                foreach (var m in _pressedElement.Manipulators) m.OnMouseDown();
            }
        }
        
        if (Input.IsMouseLeftButtonUp)
        {
            if (_pressedElement != null)
            {
                _pressedElement.IsActive = false;
                _pressedElement.MarkDirty(DirtyFlag.Style); // :active состояние изменилось
                
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
    
    // --- ФИНАЛЬНАЯ ВЕРСИЯ HITTEST: Рекурсивный обход с сортировкой по Z-Index ---
    private UIElement HitTest(UIElement element, Vector2 point)
    {
        if (element.ComputedStyle.GetValueOrDefault("display") == "none")
        {
            return null;
        }

        // 1. Создаем список дочерних элементов для проверки.
        
        // 2. Сортируем детей по z-index. Элементы с большим z-index должны проверяться первыми.
        var childrenToCheck = element.Children.ToList().OrderBy(static c => c.GetPosition() == "static" ? 0 : 1) // Сначала непозиционированные
            .ThenBy(static c => GetZIndex(c));

        // 3. Проверяем детей в отсортированном порядке.
        // Дети всегда проверяются ПЕРЕД родителем.
        foreach (var child in childrenToCheck.Reverse<UIElement>())
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
    private static int GetZIndex(UIElement element)
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