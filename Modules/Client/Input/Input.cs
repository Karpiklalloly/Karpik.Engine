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

    public event Action<Key>? KeyPressed;
    public event Action<Key>? KeyUnPressed;
    public event Action<Key>? KeyPressing;
    
    public event Action<char>? CharPressed;
    public event Action<char>? CharUnPressed;
    public event Action<char>? CharPressing;

    private ConcurrentDictionary<Key, State> _keyStates = new();
    private ConcurrentDictionary<char, State> _charStates = new();
    private Vector2 _mousePosition = Vector2.Zero;
    private Vector2 _mouseDelta = Vector2.Zero;
    private bool _isMouseLocked = false;
    
    private IInputSource _source;
    private InputCaptureState _captureState;
    
    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;
    
    public IEnumerable<Key> PressedKeys => _keyStates.Keys.Where(IsPressed);
    public IEnumerable<char> Chars => new List<char>(_chars);
    
    public bool IsMouseLeftButtonDown => !_captureState.Mouse && _source.IsMouseButtonPressed(MouseButton.Left);
    
    public bool IsMouseLeftButtonUp => !_captureState.Mouse && _source.IsMouseButtonReleased(MouseButton.Left);
    
    public bool IsMouseLeftButtonHold => !_captureState.Mouse && _source.IsMouseButtonDown(MouseButton.Left);
    
    public bool IsMouseRightButtonDown => !_captureState.Mouse && _source.IsMouseButtonPressed(MouseButton.Right);
    
    public bool IsMouseRightButtonUp => !_captureState.Mouse && _source.IsMouseButtonReleased(MouseButton.Right);
    
    public bool IsMouseRightButtonHold => !_captureState.Mouse && _source.IsMouseButtonDown(MouseButton.Right);
    
    public bool IsMouseMiddleButtonDown => !_captureState.Mouse && _source.IsMouseButtonPressed(MouseButton.Middle);
    
    public bool IsMouseMiddleButtonUp => !_captureState.Mouse && _source.IsMouseButtonReleased(MouseButton.Middle);
    
    public bool IsMouseMiddleButtonHold => !_captureState.Mouse && _source.IsMouseButtonDown(MouseButton.Middle);

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
    
    internal void Init(IInputSource source, InputCaptureState captureState)
    {
        _source = source;
        _captureState = captureState;
        foreach (Key key in Enum.GetValues<Key>())
        {
            _keyStates[key] = State.UpHold;
        }
    }

    internal void Destroy()
    {
        KeyPressed = null!;
        KeyUnPressed = null!;
        KeyPressing = null!;
        CharPressed = null!;
        CharUnPressed = null!;
        CharPressing = null!;
        
        _pressedKeys.Clear();
        _unpressedKeys.Clear();
        _chars.Clear();

        _source = null!;
        _captureState = null!;
    }
    
    private List<Key> _pressedKeys = new();
    private List<Key> _unpressedKeys = new();
    private List<char> _chars = new();
    
    internal void Update()
    {
        _pressedKeys.Clear();
        _unpressedKeys.Clear();
        _chars.Clear();
        
        if (!_captureState.Keyboard)
        {
            _pressedKeys.AddRange(_source.PressedKeys);
            _unpressedKeys.AddRange(_source.UnPressedKeys);
        }
        
        foreach (var key in _pressedKeys)
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
        
        foreach (var key in _unpressedKeys)
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
        
        if (!_captureState.Text)
        {
            _chars.AddRange(_source.PressedKeyChars);
        }

        for (int i = 0; i < _chars.Count; i++)
        {
            CharPressed?.Invoke(_chars[i]);
        }

        if (_captureState.Mouse)
        {
            _mouseDelta = Vector2.Zero;
        }
        else
        {
            _mouseDelta = _source.MouseDelta;
            _mousePosition = _source.MousePosition;
        }
    }
}
