using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Core;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Manipulators;

public class ClickableManipulator : Manipulator
{
    public event Action? OnClicked;
    
    private bool _wasPressed = false;
    
    public override void Update(float deltaTime)
    {
        if (Element == null || !Element.Enabled) return;
        
        var mousePos = Raylib.GetMousePosition();
        bool isHovered = Element.ContainsPoint(mousePos);
        bool isPressed = isHovered && Raylib.IsMouseButtonDown(MouseButton.Left);
        bool wasClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left) && _wasPressed;
        
        Element.HandleHover(isHovered);
        Element.HandlePress(isPressed);
        
        if (wasClicked)
        {
            Element.HandleClick();
            OnClicked?.Invoke();
        }
        
        _wasPressed = isPressed;
    }
}