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
        
    }

    public bool Handle(InputEvent inputEvent)
    {
        if (_element == null) return false;

        bool handled = false;

        if (inputEvent is { Type: InputEventType.MouseClick, MouseButton: MouseButton.Left })
        {
            if (_element.ContainsPoint(inputEvent.MousePosition))
            {
                SetFocus(_element);
                handled = true;
            }
            else if (_currentFocusedElement == _element)
            {
                // Снимаем фокус если кликнули вне элемента
                ClearFocus();
                handled = true;
            }
        }

        if (inputEvent is { Type: InputEventType.KeyDown, Key: KeyboardKey.Tab })
        {
            ClearFocus();
            // TODO: Фокуситься на следующем элементу
            handled = true;
        }
        
        if (inputEvent is { Type: InputEventType.KeyDown, Key: KeyboardKey.Escape })
        {
            ClearFocus();
            handled = true;
        }

        return handled;
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