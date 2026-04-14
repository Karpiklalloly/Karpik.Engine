using System.Collections.Concurrent;
using System.Numerics;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;

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

    public event Action<Key> KeyPressed;
    public event Action<Key> KeyUnPressed;
    public event Action<Key> KeyPressing;
    
    public event Action<char> CharPressed;
    public event Action<char> CharUnPressed;
    public event Action<char> CharPressing;

    private ConcurrentDictionary<Key, State> _keyStates = new();
    private ConcurrentDictionary<char, State> _charStates = new();
    private Vector2 _mousePosition = Vector2.Zero;
    private Vector2 _mouseDelta = Vector2.Zero;
    private bool _isMouseLocked = false;
    
    private IInputSource _source;
    
    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;
    
    public IEnumerable<Key> PressedKeys => _keyStates.Keys.Where(IsPressed);
    public IEnumerable<char> Chars => new List<char>(_chars);
    
    public bool IsMouseLeftButtonDown => _source.IsMouseButtonPressed(MouseButton.Left);
    
    public bool IsMouseLeftButtonUp => _source.IsMouseButtonReleased(MouseButton.Left);
    
    public bool IsMouseLeftButtonHold => _source.IsMouseButtonDown(MouseButton.Left);
    
    public bool IsMouseRightButtonDown => _source.IsMouseButtonPressed(MouseButton.Right);
    
    public bool IsMouseRightButtonUp => _source.IsMouseButtonReleased(MouseButton.Right);
    
    public bool IsMouseRightButtonHold => _source.IsMouseButtonDown(MouseButton.Right);
    
    public bool IsMouseMiddleButtonDown => _source.IsMouseButtonPressed(MouseButton.Middle);
    
    public bool IsMouseMiddleButtonUp => _source.IsMouseButtonReleased(MouseButton.Middle);
    
    public bool IsMouseMiddleButtonHold => _source.IsMouseButtonDown(MouseButton.Middle);

    public bool IsMouseLocked => _isMouseLocked;

    public bool IsPressed(Key key)
    {
        return _keyStates[key] is State.DownEvent;
    }
    
    public bool IsUnPressed(Key key)
    {
        return _keyStates[key] is State.UpEvent;
    }

    public bool IsPressing(Key key)
    {
        return _keyStates[key] is State.DownHold;
    }
    
    public bool IsUnPressing(Key key)
    {
        return _keyStates[key] is State.UpHold;
    }
    
    public bool IsDown(Key key)
    {
        return _keyStates[key] is State.DownHold or State.DownEvent;
    }
    
    public bool IsUp(Key key)
    {
        return _keyStates[key] is State.UpHold or State.UpEvent;
    }
    
    public void LockCursor()
    {
        _isMouseLocked = true;
        _source.DisableCursor();
    }
    
    public void UnlockCursor()
    {
        _isMouseLocked = false;
        _source.EnableCursor();
    }
    
    internal void Init(IInputSource source)
    {
        _source = source;
    }

    internal void Destroy()
    {
        KeyPressed = null!;
        KeyUnPressed = null!;
        KeyPressing = null!;
        CharPressed = null!;
        CharUnPressed = null!;
        CharPressing = null!;
        
        _keys.Clear();
        _chars.Clear();

        _source = null!;
    }
    
    private List<Key> _keys = new();
    private List<char> _chars = new();
    
    internal void Update()
    {
        _keys.Clear();
        _chars.Clear();
        
        _keys.AddRange(_source.PressedKeys);
        
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
        
        _chars.AddRange(_source.PressedKeyChars);
        for (int i = 0; i < _chars.Count; i++)
        {
            CharPressed?.Invoke(_chars[i]);
        }

        _mouseDelta = _source.MouseDelta;
        _mousePosition = _source.MousePosition;
    }
}