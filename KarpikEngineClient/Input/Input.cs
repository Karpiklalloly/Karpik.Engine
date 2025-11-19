using System.Collections.Concurrent;
using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class Input
{
    private enum State
    {
        DownEvent,
        DownHold,
        UpEvent,
        UpHold
    }

    public event Action<KeyboardKey> KeyPressed;
    public event Action<KeyboardKey> KeyUnPressed;
    public event Action<KeyboardKey> KeyPressing;
    
    public event Action<char> CharPressed;
    public event Action<char> CharUnPressed;
    public event Action<char> CharPressing;
    
    private ConcurrentDictionary<KeyboardKey, State> _keyStates = new();
    private ConcurrentDictionary<char, State> _charStates = new();
    private Vector2 _mousePosition = Vector2.Zero;
    private Vector2 _mouseDelta = Vector2.Zero;
    private bool _isMouseLocked = false;
    
    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;
    
    public IEnumerable<KeyboardKey> PressedKeys => _keyStates.Keys.Where(IsPressed);
    
    public bool IsMouseLeftButtonDown => Raylib.IsMouseButtonPressed(MouseButton.Left);
    
    public bool IsMouseLeftButtonUp => Raylib.IsMouseButtonReleased(MouseButton.Left);
    
    public bool IsMouseLeftButtonHold => Raylib.IsMouseButtonDown(MouseButton.Left);
    
    public bool IsMouseRightButtonDown => Raylib.IsMouseButtonPressed(MouseButton.Right);
    
    public bool IsMouseRightButtonUp => Raylib.IsMouseButtonReleased(MouseButton.Right);
    
    public bool IsMouseRightButtonHold => Raylib.IsMouseButtonDown(MouseButton.Right);
    
    public bool IsMouseMiddleButtonDown => Raylib.IsMouseButtonPressed(MouseButton.Middle);
    
    public bool IsMouseMiddleButtonUp => Raylib.IsMouseButtonReleased(MouseButton.Middle);
    
    public bool IsMouseMiddleButtonHold => Raylib.IsMouseButtonDown(MouseButton.Middle);

    public bool IsMouseLocked => _isMouseLocked;

    public bool IsPressed(KeyboardKey key)
    {
        return Raylib.IsKeyPressed(key);
    }
    
    public bool IsUnPressed(KeyboardKey key)
    {
        return Raylib.IsKeyReleased(key);
    }

    public bool IsPressing(KeyboardKey key)
    {
        return Raylib.IsKeyDown(key) && !Raylib.IsKeyPressed(key);
    }
    
    public bool IsUnPressing(KeyboardKey key)
    {
        return Raylib.IsKeyUp(key) && !Raylib.IsKeyReleased(key);
    }
    
    public bool IsDown(KeyboardKey key)
    {
        return Raylib.IsKeyDown(key);
    }
    
    public bool IsUp(KeyboardKey key)
    {
        return Raylib.IsKeyUp(key);
    }
    
    public void LockCursor()
    {
        _isMouseLocked = true;
        Raylib.DisableCursor();
    }
    
    public void UnlockCursor()
    {
        _isMouseLocked = false;
        Raylib.EnableCursor();
    }

    private List<KeyboardKey> _keys = new();
    private List<char> _chars = new();
    internal void Update()
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

        _mousePosition = Raylib.GetMousePosition();
        _mouseDelta = Raylib.GetMouseDelta();
        Console.WriteLine(_mouseDelta);
    }
}