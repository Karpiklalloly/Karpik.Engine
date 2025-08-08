using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Manipulators;

public class FocusManipulator : IManipulator
{
    private VisualElement? _element;
    private static VisualElement? _currentFocusedElement;
    
    public void Attach(VisualElement element)
    {
        _element = element;
    }
    
    public void Detach(VisualElement element)
    {
        if (_currentFocusedElement == element)
        {
            _currentFocusedElement = null;
            element.HandleFocus(false);
        }
        _element = null;
    }
    
    public void Update(float deltaTime)
    {
        if (_element == null) return;
        
        // Проверяем клик мыши для установки/снятия фокуса
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var mousePos = Raylib.GetMousePosition();
            
            if (_element.ContainsPoint(mousePos))
            {
                // Устанавливаем фокус на этот элемент
                SetFocus(_element);
            }
            else if (_currentFocusedElement == _element)
            {
                // Снимаем фокус если кликнули вне элемента
                ClearFocus();
            }
        }
        
        // Проверяем Tab для переключения фокуса
        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            // Простая реализация - снимаем фокус
            // В более сложной реализации можно переключаться между элементами
            ClearFocus();
        }
        
        // Проверяем Escape для снятия фокуса
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            ClearFocus();
        }
    }
    
    public static void SetFocus(VisualElement element)
    {
        if (_currentFocusedElement == element) return;
        
        // Снимаем фокус с предыдущего элемента
        if (_currentFocusedElement != null)
        {
            _currentFocusedElement.HandleFocus(false);
        }
        
        // Устанавливаем фокус на новый элемент
        _currentFocusedElement = element;
        element.HandleFocus(true);
    }
    
    public static void ClearFocus()
    {
        if (_currentFocusedElement != null)
        {
            _currentFocusedElement.HandleFocus(false);
            _currentFocusedElement = null;
        }
    }
    
    public static bool HasFocus(VisualElement element)
    {
        return _currentFocusedElement == element;
    }
}