using System.Numerics;
using Karpik.Engine.Client.UIToolkit;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Manipulators;

public class ClickableManipulator : Manipulator
{
    public event Action? OnClicked;
    
    private bool _wasPressed = false;

    public override void Update(double deltaTime)
    {
        
    }

    public override bool Handle(InputEvent inputEvent)
    {
        if (inputEvent.Type == InputEventType.MouseClick &&
            inputEvent.MouseButton == MouseButton.Left &&
            Element.ContainsPoint(inputEvent.MousePosition))
        {
            OnClicked?.Invoke();
            return true;
        }

        return false;
    }

    // Этот метод вызывается из Button.HandleSelfInputEvent
    public void TriggerClick()
    {
        
    }
}