using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Manipulators;

public class DragManipulator : IManipulator
{
    private VisualElement? _element;
    private bool _isDragging = false;
    private Vector2 _dragOffset;
    
    public void Attach(VisualElement element)
    {
        _element = element;
    }
    
    public void Detach(VisualElement element)
    {
        _element = null;
        _isDragging = false;
    }
    
    public void Update(float deltaTime)
    {
        
    }

    public bool Handle(InputEvent inputEvent)
    {
        if (_element == null) return false;

        bool handled = true;
        var mousePos = inputEvent.MousePosition;
        
        if (inputEvent is { Type: InputEventType.MouseDown, MouseButton: MouseButton.Left })
        {
            if (_element.ContainsPoint(mousePos))
            {
                _isDragging = true;
                _dragOffset = inputEvent.MouseDelta;
                handled = true;
            }
        }

        if (_isDragging)
        {
            // Перетаскиваем родительский элемент (модальное окно)
            if (_element.Parent != null)
            {
                _element.Parent.Position = mousePos - _dragOffset;
                handled = true;
            }
        }

        if (inputEvent is { Type: InputEventType.MouseUp, MouseButton: MouseButton.Left })
        {
            _isDragging = false;
            handled = true;
        }

        return handled;
    }
}