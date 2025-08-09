using System.Numerics;
using Karpik.Engine.Client.UIToolkit;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Manipulators;

public class ClickableManipulator : Manipulator
{
    public event Action? OnClicked;
    
    private bool _wasPressed = false;
    
    public override void Update(float deltaTime)
    {
        // Манипулятор теперь работает через систему событий
        // Основная логика обработки кликов перенесена в HandleSelfInputEvent элементов
        
        if (Element == null || !Element.Enabled) return;
        
        // Обновляем состояние hover на основе текущей позиции мыши
        var mousePos = Raylib.GetMousePosition();
        bool isHovered = Element.ContainsPoint(mousePos);
        Element.HandleHover(isHovered);
    }
    
    // Этот метод вызывается из Button.HandleSelfInputEvent
    public void TriggerClick()
    {
        Console.WriteLine($"Button clicked via manipulator: {Element?.Name}");
        OnClicked?.Invoke();
    }
}