using System.Collections.Concurrent;
using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client;

public static class Input
{
    private enum State
    {
        DownEvent,
        DownHold,
        UpEvent,
        UpHold
    }

    public static event Action<KeyboardKey> KeyPressed;
    public static event Action<KeyboardKey> KeyUnPressed;
    public static event Action<KeyboardKey> KeyPressing;
    
    public static event Action<char> CharPressed;
    public static event Action<char> CharUnPressed;
    public static event Action<char> CharPressing;
    
    private static ConcurrentDictionary<KeyboardKey, State> _keyStates = new();
    private static ConcurrentDictionary<char, State> _charStates = new();
    
    public static Vector2 MousePosition => Raylib.GetMousePosition();
    public static Vector2 MouseDelta => Raylib.GetMouseDelta();
    
    public static IEnumerable<KeyboardKey> PressedKeys => _keyStates.Keys.Where(IsPressed);
    
    public static bool IsMouseLeftButtonDown => Raylib.IsMouseButtonPressed(MouseButton.Left);
    
    public static bool IsMouseLeftButtonUp => Raylib.IsMouseButtonReleased(MouseButton.Left);
    
    public static bool IsMouseLeftButtonHold => Raylib.IsMouseButtonDown(MouseButton.Left);
    
    public static bool IsMouseRightButtonDown => Raylib.IsMouseButtonPressed(MouseButton.Right);
    
    public static bool IsMouseRightButtonUp => Raylib.IsMouseButtonReleased(MouseButton.Right);
    
    public static bool IsMouseRightButtonHold => Raylib.IsMouseButtonDown(MouseButton.Right);
    
    public static bool IsMouseMiddleButtonDown => Raylib.IsMouseButtonPressed(MouseButton.Middle);
    
    public static bool IsMouseMiddleButtonUp => Raylib.IsMouseButtonReleased(MouseButton.Middle);
    
    public static bool IsMouseMiddleButtonHold => Raylib.IsMouseButtonDown(MouseButton.Middle);
    
    public static bool IsPressed(KeyboardKey key)
    {
        return Raylib.IsKeyPressed(key);
    }
    
    public static bool IsUnPressed(KeyboardKey key)
    {
        return Raylib.IsKeyReleased(key);
    }

    public static bool IsPressing(KeyboardKey key)
    {
        return Raylib.IsKeyDown(key) && !Raylib.IsKeyPressed(key);
    }
    
    public static bool IsUnPressing(KeyboardKey key)
    {
        return Raylib.IsKeyUp(key) && !Raylib.IsKeyReleased(key);
    }
    
    public static bool IsDown(KeyboardKey key)
    {
        return Raylib.IsKeyDown(key);
    }
    
    public static bool IsUp(KeyboardKey key)
    {
        return Raylib.IsKeyUp(key);
    }

    private static List<KeyboardKey> _keys = new();
    private static List<char> _chars = new();
    internal static void Update()
    {
        _keys.Clear();
        _chars.Clear();
        
        while (true)
        {
            var key = (KeyboardKey)Raylib.GetKeyPressed();
            if (key == KeyboardKey.Null) break;
            _keys.Add(key);
        }
        
        foreach (var key in _keys)
        {
            if (_keyStates.ContainsKey(key))
            {
                if (_keyStates[key] == State.DownEvent)
                {
                    _keyStates[key] = State.DownHold;
                    KeyPressing?.Invoke(key);
                }
                if (_keyStates[key] == State.UpEvent || _keyStates[key] == State.UpHold)
                {
                    _keyStates[key] = State.DownEvent;
                    KeyPressed?.Invoke(key);
                    KeyPressing?.Invoke(key);
                }
            }
            
            if (!_keyStates.ContainsKey(key))
            {
                _keyStates.TryAdd(key, State.DownEvent);
                KeyPressed?.Invoke(key);
                KeyPressing?.Invoke(key);
            }
        }
        
        foreach (var key in _keyStates.Keys.Except(_keys))
        {
            if (_keyStates[key] == State.DownEvent || _keyStates[key] == State.DownHold)
            {
                _keyStates[key] = State.UpEvent;
                KeyUnPressed?.Invoke(key);
            }

            if (_keyStates[key] == State.UpEvent)
            {
                _keyStates[key] = State.UpHold;
            }
        }

        while (true)
        {
            var key = Raylib.GetCharPressed();
            if (key == 0) break;
            CharPressed?.Invoke((char)key);
            _chars.Add((char)key);
        }
    }
}