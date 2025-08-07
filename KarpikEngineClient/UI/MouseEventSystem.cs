using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class MouseEventSystem
{
    private VisualElement _hoveredElement;
    private VisualElement _pressedElement;
    private Vector2 _lastMousePosition;
    
    public void Update(VisualElement rootElement)
    {
        var mousePos = Raylib.GetMousePosition();
        var mouseButton = MouseButton.Left;
        
        // Обновляем состояние hover
        UpdateHoverState(rootElement, mousePos);
        
        // Обработка нажатий
        if (Raylib.IsMouseButtonPressed(mouseButton))
        {
            HandleMouseDown(rootElement, mousePos, mouseButton);
        }
        
        if (Raylib.IsMouseButtonReleased(mouseButton))
        {
            HandleMouseUp(rootElement, mousePos, mouseButton);
        }
        
        _lastMousePosition = mousePos;
    }
    
    private void UpdateHoverState(VisualElement element, Vector2 mousePos)
    {
        if (!element.Visible || !element.Enabled) return;
        
        // Проверяем, находится ли курсор над элементом
        bool isOverElement = IsPointInElement(element, mousePos);
        
        if (isOverElement)
        {
            // Если элемент еще не помечен как hovered
            if (!element.IsHovered)
            {
                element.HandleMouseEnter();
                
                // Вызываем событие MouseLeave у предыдущего hovered элемента
                if (_hoveredElement != null && _hoveredElement != element)
                {
                    _hoveredElement.HandleMouseLeave();
                }
                
                _hoveredElement = element;
            }
        }
        else
        {
            // Если курсор ушел с элемента
            if (element.IsHovered)
            {
                element.HandleMouseLeave();
                if (_hoveredElement == element)
                {
                    _hoveredElement = null;
                }
            }
        }
        
        // Рекурсивно проверяем детей
        foreach (var child in element.Children)
        {
            UpdateHoverState(child, mousePos);
        }
    }
    
    private void HandleMouseDown(VisualElement element, Vector2 mousePos, MouseButton button)
    {
        var mouseEvent = new MouseEvent(mousePos, button);
        
        // Ищем элемент под курсором (самый верхний)
        var targetElement = FindElementAtPoint(element, mousePos);
        
        if (targetElement != null)
        {
            _pressedElement = targetElement;
            targetElement.HandleMouseDown(mouseEvent);
        }
    }
    
    private void HandleMouseUp(VisualElement element, Vector2 mousePos, MouseButton button)
    {
        var mouseEvent = new MouseEvent(mousePos, button);
        
        // Ищем элемент под курсором
        var targetElement = FindElementAtPoint(element, mousePos);
        
        if (_pressedElement != null)
        {
            _pressedElement.HandleMouseUp(mouseEvent);
            
            // Если released над тем же элементом, что и pressed - это клик
            if (targetElement == _pressedElement)
            {
                _pressedElement.HandleClick(mouseEvent);
            }
            
            _pressedElement = null;
        }
    }
    
    private bool IsPointInElement(VisualElement element, Vector2 point)
    {
        var rect = new Rectangle(element.Position.X, element.Position.Y, 
                               element.Size.X, element.Size.Y);
        return Raylib.CheckCollisionPointRec(point, rect);
    }
    
    private VisualElement FindElementAtPoint(VisualElement element, Vector2 point)
    {
        if (!element.Visible || !element.Enabled) return null;
        
        // Проверяем сначала детей (они сверху)
        for (int i = element.Children.Count - 1; i >= 0; i--)
        {
            var child = element.Children[i];
            var found = FindElementAtPoint(child, point);
            if (found != null)
                return found;
        }
        
        // Проверяем сам элемент
        if (IsPointInElement(element, point))
        {
            return element;
        }
        
        return null;
    }
}