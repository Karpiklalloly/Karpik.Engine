using System.Collections.Concurrent;
using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;

namespace Karpik.Engine.Client.InputModule;

public class Input
{
    private enum State
    {
        DownEvent,
        DownHold,
        UpEvent,
        UpHold
    }

    public event Action<KeyboardKeys> KeyPressed;
    public event Action<KeyboardKeys> KeyUnPressed;
    public event Action<KeyboardKeys> KeyPressing;
    
    public event Action<char> CharPressed;
    public event Action<char> CharUnPressed;
    public event Action<char> CharPressing;

    private Queue<KeyboardKeys> _keysQueue = new();
    private ConcurrentDictionary<KeyboardKeys, State> _keyStates = new();
    private ConcurrentDictionary<char, State> _charStates = new();
    private Vector2 _mousePosition = Vector2.Zero;
    private Vector2 _mouseDelta = Vector2.Zero;
    private bool _isMouseLocked = false;
    
    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;

    public Queue<KeyboardKeys> Keys => new(_keysQueue);
    
    public IEnumerable<KeyboardKeys> PressedKeys => _keyStates.Keys.Where(IsPressed);
    public IEnumerable<char> Chars => new List<char>(_chars);
    
    public bool IsMouseLeftButtonDown => _window.IsMouseButtonPressed((int)MouseButtons.Left);
    
    public bool IsMouseLeftButtonUp => _window.IsMouseButtonReleased((int)MouseButtons.Left);
    
    public bool IsMouseLeftButtonHold => _window.IsMouseButtonDown((int)MouseButtons.Left);
    
    public bool IsMouseRightButtonDown => _window.IsMouseButtonPressed((int)MouseButtons.Right);
    
    public bool IsMouseRightButtonUp => _window.IsMouseButtonReleased((int)MouseButtons.Right);
    
    public bool IsMouseRightButtonHold => _window.IsMouseButtonDown((int)MouseButtons.Right);
    
    public bool IsMouseMiddleButtonDown => _window.IsMouseButtonPressed((int)MouseButtons.Middle);
    
    public bool IsMouseMiddleButtonUp => _window.IsMouseButtonReleased((int)MouseButtons.Middle);
    
    public bool IsMouseMiddleButtonHold => _window.IsMouseButtonDown((int)MouseButtons.Middle);

    public bool IsMouseLocked => _isMouseLocked;

    public bool IsPressed(KeyboardKeys key)
    {
        return _window.IsKeyPressed((int)key);
    }
    
    public bool IsUnPressed(KeyboardKeys key)
    {
        return _window.IsKeyReleased((int)key);
    }

    public bool IsPressing(KeyboardKeys key)
    {
        return _window.IsKeyDown((int)key) && !_window.IsKeyPressed((int)key);
    }
    
    public bool IsUnPressing(KeyboardKeys key)
    {
        return _window.IsKeyUp((int)key) && !_window.IsKeyReleased((int)key);
    }
    
    public bool IsDown(KeyboardKeys key)
    {
        return _window.IsKeyDown((int)key);
    }
    
    public bool IsUp(KeyboardKeys key)
    {
        return _window.IsKeyUp((int)key);
    }
    
    public void LockCursor()
    {
        _isMouseLocked = true;
        _window.DisableCursor();
    }
    
    public void UnlockCursor()
    {
        _isMouseLocked = false;
        _window.EnableCursor();
    }

    private List<KeyboardKeys> _keys = new();
    private List<char> _chars = new();
    private IWindow _window;

    internal void Init(IWindow window)
    {
        _window = window;
    }

    internal void Destory()
    {
        KeyPressed = null!;
        KeyUnPressed = null!;
        KeyPressing = null!;
        CharPressed = null!;
        CharUnPressed = null!;
        CharPressing = null!;
        
        _keys.Clear();
        _chars.Clear();

        _window = null!;
    }
    
    internal void Update()
    {
        _keysQueue.Clear();
        _keys.Clear();
        _chars.Clear();
        
        while (true)
        {
            var key = (KeyboardKeys)_window.GetKeyPressed();
            _keysQueue.Enqueue(key);
            if (key == KeyboardKeys.Null) break;
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
            var key = _window.GetCharPressed();
            if (key == 0) break;
            CharPressed?.Invoke(key);
            _chars.Add(key);
        }

        _mousePosition = _window.GetMousePosition();
        _mouseDelta = _window.GetMouseDelta();
    }
}