using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public enum InputEventType
{
    MouseMove,
    MouseDown,
    MouseUp,
    MouseClick,
    KeyDown,
    KeyUp,
    TextInput
}

public class InputEvent
{
    public InputEventType Type { get; set; }
    public Vector2 MousePosition { get; set; }
    public MouseButton MouseButton { get; set; }
    public KeyboardKey Key { get; set; }
    public char Character { get; set; }
    public bool Handled { get; set; } = false;
    
    public static InputEvent MouseMove(Vector2 position)
    {
        return new InputEvent
        {
            Type = InputEventType.MouseMove,
            MousePosition = position
        };
    }
    
    public static InputEvent MouseDown(Vector2 position, MouseButton button)
    {
        return new InputEvent
        {
            Type = InputEventType.MouseDown,
            MousePosition = position,
            MouseButton = button
        };
    }
    
    public static InputEvent MouseUp(Vector2 position, MouseButton button)
    {
        return new InputEvent
        {
            Type = InputEventType.MouseUp,
            MousePosition = position,
            MouseButton = button
        };
    }
    
    public static InputEvent MouseClick(Vector2 position, MouseButton button)
    {
        return new InputEvent
        {
            Type = InputEventType.MouseClick,
            MousePosition = position,
            MouseButton = button
        };
    }
    
    public static InputEvent KeyDown(KeyboardKey key)
    {
        return new InputEvent
        {
            Type = InputEventType.KeyDown,
            Key = key
        };
    }
    
    public static InputEvent KeyUp(KeyboardKey key)
    {
        return new InputEvent
        {
            Type = InputEventType.KeyUp,
            Key = key
        };
    }
    
    public static InputEvent TextInput(char character)
    {
        return new InputEvent
        {
            Type = InputEventType.TextInput,
            Character = character
        };
    }
}

public interface IInputHandler
{
    bool HandleInput(InputEvent inputEvent);
}