using System.Composition;
using System.Numerics;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;

namespace Karpik.Engine.Client.InputModule;

[Export(typeof(Input))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class Input
{
    private const int DefaultEventCapacity = 256;
    private const int KeyCount = (int)Key.LastKey + 1;
    private const int MouseButtonCount = (int)MouseButton.LastButton + 1;

    private static readonly Key[] Keys = Enum.GetValues<Key>();
    private static readonly MouseButton[] MouseButtons = Enum.GetValues<MouseButton>();

    public event Action<Key>? KeyPressed;
    public event Action<Key>? KeyReleased;
    public event Action<Key>? KeyUnPressed;
    public event Action<Key>? KeyPressing;

    public event Action<char>? TextInput;
    public event Action<char>? CharPressed;
    public event Action<char>? CharUnPressed;
    public event Action<char>? CharPressing;

    private readonly int[] _publishedKeyDown = new int[KeyCount];
    private readonly int[] _publishedMouseDown = new int[MouseButtonCount];

    private readonly bool[] _keyDown = new bool[KeyCount];
    private readonly bool[] _keyPressed = new bool[KeyCount];
    private readonly bool[] _keyReleased = new bool[KeyCount];

    private readonly bool[] _mouseDown = new bool[MouseButtonCount];
    private readonly bool[] _mousePressed = new bool[MouseButtonCount];
    private readonly bool[] _mouseReleased = new bool[MouseButtonCount];

    private readonly List<Key> _pressedKeys = new(KeyCount);
    private readonly List<char> _chars = new(32);
    private readonly InputEventRing _events = new(DefaultEventCapacity);

    private int _publishedMousePositionXBits;
    private int _publishedMousePositionYBits;
    private int _publishedMouseDeltaXBits;
    private int _publishedMouseDeltaYBits;
    private int _publishedMouseCaptured;
    private Vector2 _mousePosition = Vector2.Zero;
    private Vector2 _mouseDelta = Vector2.Zero;
    private bool _mouseCaptured;
    private bool _isMouseLocked;
    private int _cursorRequest;

    private IInputSource _source = null!;
    private InputCaptureState _captureState = null!;

    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;

    public IEnumerable<Key> PressedKeys => _pressedKeys;
    public IEnumerable<char> Chars => _chars;

    public bool IsMouseLeftButtonDown => IsMouseButtonPressed(MouseButton.Left);
    public bool IsMouseLeftButtonUp => IsMouseButtonReleased(MouseButton.Left);
    public bool IsMouseLeftButtonHold => IsMouseButtonDown(MouseButton.Left);

    public bool IsMouseRightButtonDown => IsMouseButtonPressed(MouseButton.Right);
    public bool IsMouseRightButtonUp => IsMouseButtonReleased(MouseButton.Right);
    public bool IsMouseRightButtonHold => IsMouseButtonDown(MouseButton.Right);

    public bool IsMouseMiddleButtonDown => IsMouseButtonPressed(MouseButton.Middle);
    public bool IsMouseMiddleButtonUp => IsMouseButtonReleased(MouseButton.Middle);
    public bool IsMouseMiddleButtonHold => IsMouseButtonDown(MouseButton.Middle);

    public bool IsMouseLocked => _isMouseLocked;
    internal bool OverflowedLastFrame { get; private set; }

    public Input(IInputSource source, InputCaptureState captureState)
    {
        _source = source;
        _captureState = captureState;
        ClearAllState();
    }

    public bool IsPressed(Key key)
    {
        int index = KeyIndex(key);
        return index >= 0 && _keyPressed[index];
    }

    public bool IsUnPressed(Key key)
    {
        int index = KeyIndex(key);
        return index >= 0 && _keyReleased[index];
    }

    public bool IsPressing(Key key)
    {
        return IsDown(key);
    }

    public bool IsUnPressing(Key key)
    {
        return IsUp(key);
    }

    public bool IsDown(Key key)
    {
        int index = KeyIndex(key);
        return index >= 0 && _keyDown[index];
    }

    public bool IsUp(Key key)
    {
        int index = KeyIndex(key);
        return index < 0 || !_keyDown[index];
    }

    public void LockCursor()
    {
        _isMouseLocked = true;
        Volatile.Write(ref _cursorRequest, (int)CursorRequest.Locked);
    }

    public void UnlockCursor()
    {
        _isMouseLocked = false;
        Volatile.Write(ref _cursorRequest, (int)CursorRequest.Unlocked);
    }

    internal void Destroy()
    {
        KeyPressed = null!;
        KeyReleased = null!;
        KeyUnPressed = null!;
        KeyPressing = null!;
        TextInput = null!;
        CharPressed = null!;
        CharUnPressed = null!;
        CharPressing = null!;

        ClearAllState();
        _events.Clear();
        _source = null!;
        _captureState = null!;
    }

    internal void Update()
    {
        PublishPlatformFrame();
        ConsumeSimulationFrame();
    }

    internal void PublishPlatformFrame()
    {
        ApplyCursorRequest();
        PublishFromSource();
    }

    internal void ConsumeSimulationFrame()
    {
        BeginSimulationFrame();
    }

    private void PublishFromSource()
    {
        bool mouseCaptured = _captureState.Mouse;
        Volatile.Write(ref _publishedMouseCaptured, mouseCaptured ? 1 : 0);

        if (!_captureState.Keyboard)
        {
            PublishPressedKeys(_source.PressedKeys);
            PublishReleasedKeys(_source.UnPressedKeys);
        }

        if (!_captureState.Text)
        {
            IReadOnlyList<char> pressedChars = _source.PressedKeyChars;
            for (int i = 0; i < pressedChars.Count; i++)
            {
                _events.TryEnqueue(InputEvent.Text(pressedChars[i]));
            }
        }

        if (mouseCaptured)
        {
            Volatile.Write(ref _publishedMouseDeltaXBits, 0);
            Volatile.Write(ref _publishedMouseDeltaYBits, 0);
        }
        else
        {
            PublishMouseState();
            PublishMouseSnapshot(_source.MousePosition, _source.MouseDelta);
        }
    }

    private void PublishPressedKeys(IReadOnlyList<Key> keys)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            Key key = keys[i];
            int index = KeyIndex(key);
            if (index < 0)
            {
                continue;
            }

            Volatile.Write(ref _publishedKeyDown[index], 1);
            _events.TryEnqueue(InputEvent.KeyPressed(key));
        }
    }

    private void PublishReleasedKeys(IReadOnlyList<Key> keys)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            Key key = keys[i];
            int index = KeyIndex(key);
            if (index < 0)
            {
                continue;
            }

            Volatile.Write(ref _publishedKeyDown[index], 0);
            _events.TryEnqueue(InputEvent.KeyReleased(key));
        }
    }

    private void PublishMouseState()
    {
        for (int i = 0; i < MouseButtons.Length; i++)
        {
            MouseButton button = MouseButtons[i];
            int index = MouseButtonIndex(button);
            if (index < 0)
            {
                continue;
            }

            bool wasDown = Volatile.Read(ref _publishedMouseDown[index]) != 0;
            bool isDown = _source.IsMouseButtonDown(button);
            Volatile.Write(ref _publishedMouseDown[index], isDown ? 1 : 0);

            if (isDown && !wasDown)
            {
                _events.TryEnqueue(InputEvent.MousePressed(button));
            }
            else if (!isDown && wasDown)
            {
                _events.TryEnqueue(InputEvent.MouseReleased(button));
            }
        }
    }

    private void BeginSimulationFrame()
    {
        Array.Clear(_keyPressed);
        Array.Clear(_keyReleased);
        Array.Clear(_mousePressed);
        Array.Clear(_mouseReleased);
        _pressedKeys.Clear();
        _chars.Clear();
        OverflowedLastFrame = false;

        _mouseCaptured = Volatile.Read(ref _publishedMouseCaptured) != 0;
        _mousePosition = ReadPublishedVector(ref _publishedMousePositionXBits, ref _publishedMousePositionYBits);
        _mouseDelta = ReadPublishedVector(ref _publishedMouseDeltaXBits, ref _publishedMouseDeltaYBits);

        while (_events.TryDequeue(out InputEvent inputEvent))
        {
            ApplyEvent(inputEvent);
        }
    }

    private void ApplyEvent(InputEvent inputEvent)
    {
        switch (inputEvent.Kind)
        {
            case InputEventKind.KeyPressed:
                ApplyKeyPressed(inputEvent.Key);
                break;
            case InputEventKind.KeyReleased:
                ApplyKeyReleased(inputEvent.Key);
                break;
            case InputEventKind.MousePressed:
                ApplyMousePressed(inputEvent.MouseButton);
                break;
            case InputEventKind.MouseReleased:
                ApplyMouseReleased(inputEvent.MouseButton);
                break;
            case InputEventKind.TextInput:
                _chars.Add(inputEvent.Character);
                TextInput?.Invoke(inputEvent.Character);
                CharPressed?.Invoke(inputEvent.Character);
                break;
            case InputEventKind.Overflow:
                ResynchronizeFromPublishedSnapshot();
                OverflowedLastFrame = true;
                break;
        }
    }

    private void ApplyKeyPressed(Key key)
    {
        int index = KeyIndex(key);
        if (index < 0)
        {
            return;
        }

        _keyDown[index] = true;
        _keyPressed[index] = true;
        _pressedKeys.Add(key);
        KeyPressed?.Invoke(key);
        KeyPressing?.Invoke(key);
    }

    private void ApplyKeyReleased(Key key)
    {
        int index = KeyIndex(key);
        if (index < 0)
        {
            return;
        }

        _keyDown[index] = false;
        _keyReleased[index] = true;
        KeyReleased?.Invoke(key);
        KeyUnPressed?.Invoke(key);
    }

    private void ApplyMousePressed(MouseButton button)
    {
        int index = MouseButtonIndex(button);
        if (index < 0)
        {
            return;
        }

        _mouseDown[index] = true;
        _mousePressed[index] = true;
    }

    private void ApplyMouseReleased(MouseButton button)
    {
        int index = MouseButtonIndex(button);
        if (index < 0)
        {
            return;
        }

        _mouseDown[index] = false;
        _mouseReleased[index] = true;
    }

    private void ResynchronizeFromPublishedSnapshot()
    {
        Array.Clear(_keyPressed);
        Array.Clear(_keyReleased);
        Array.Clear(_mousePressed);
        Array.Clear(_mouseReleased);
        _pressedKeys.Clear();
        for (int i = 0; i < _publishedKeyDown.Length; i++)
        {
            _keyDown[i] = Volatile.Read(ref _publishedKeyDown[i]) != 0;
        }

        for (int i = 0; i < _publishedMouseDown.Length; i++)
        {
            _mouseDown[i] = Volatile.Read(ref _publishedMouseDown[i]) != 0;
        }
    }

    private void ApplyCursorRequest()
    {
        switch ((CursorRequest)Interlocked.Exchange(ref _cursorRequest, (int)CursorRequest.None))
        {
            case CursorRequest.Locked:
                _source.DisableCursor();
                break;
            case CursorRequest.Unlocked:
                _source.EnableCursor();
                break;
        }

    }

    private bool IsMouseButtonPressed(MouseButton button)
    {
        int index = MouseButtonIndex(button);
        return !_mouseCaptured && index >= 0 && _mousePressed[index];
    }

    private bool IsMouseButtonReleased(MouseButton button)
    {
        int index = MouseButtonIndex(button);
        return !_mouseCaptured && index >= 0 && _mouseReleased[index];
    }

    private bool IsMouseButtonDown(MouseButton button)
    {
        int index = MouseButtonIndex(button);
        return !_mouseCaptured && index >= 0 && _mouseDown[index];
    }

    private void ClearAllState()
    {
        Array.Clear(_publishedKeyDown);
        Array.Clear(_publishedMouseDown);
        Array.Clear(_keyDown);
        Array.Clear(_keyPressed);
        Array.Clear(_keyReleased);
        Array.Clear(_mouseDown);
        Array.Clear(_mousePressed);
        Array.Clear(_mouseReleased);
        _pressedKeys.Clear();
        _chars.Clear();
        _publishedMousePositionXBits = 0;
        _publishedMousePositionYBits = 0;
        _publishedMouseDeltaXBits = 0;
        _publishedMouseDeltaYBits = 0;
        _publishedMouseCaptured = 0;
        _mousePosition = Vector2.Zero;
        _mouseDelta = Vector2.Zero;
        _mouseCaptured = false;
        _isMouseLocked = false;
        _cursorRequest = (int)CursorRequest.None;
        OverflowedLastFrame = false;
    }

    private void PublishMouseSnapshot(in Vector2 position, in Vector2 delta)
    {
        Volatile.Write(ref _publishedMousePositionXBits, BitConverter.SingleToInt32Bits(position.X));
        Volatile.Write(ref _publishedMousePositionYBits, BitConverter.SingleToInt32Bits(position.Y));
        Volatile.Write(ref _publishedMouseDeltaXBits, BitConverter.SingleToInt32Bits(delta.X));
        Volatile.Write(ref _publishedMouseDeltaYBits, BitConverter.SingleToInt32Bits(delta.Y));
    }

    private static Vector2 ReadPublishedVector(ref int xBits, ref int yBits)
    {
        return new Vector2(
            BitConverter.Int32BitsToSingle(Volatile.Read(ref xBits)),
            BitConverter.Int32BitsToSingle(Volatile.Read(ref yBits)));
    }

    private static int KeyIndex(Key key)
    {
        int index = (int)key;
        return index >= 0 && index < KeyCount ? index : -1;
    }

    private static int MouseButtonIndex(MouseButton button)
    {
        int index = (int)button;
        return index >= 0 && index < MouseButtonCount ? index : -1;
    }

    private enum CursorRequest : byte
    {
        None,
        Locked,
        Unlocked
    }
}

internal enum InputEventKind : byte
{
    KeyPressed,
    KeyReleased,
    MousePressed,
    MouseReleased,
    TextInput,
    Overflow
}

internal readonly struct InputEvent
{
    public readonly InputEventKind Kind;
    public readonly Key Key;
    public readonly MouseButton MouseButton;
    public readonly char Character;

    private InputEvent(InputEventKind kind, Key key, MouseButton mouseButton, char character)
    {
        Kind = kind;
        Key = key;
        MouseButton = mouseButton;
        Character = character;
    }

    public static InputEvent KeyPressed(Key key)
    {
        return new InputEvent(InputEventKind.KeyPressed, key, default, default);
    }

    public static InputEvent KeyReleased(Key key)
    {
        return new InputEvent(InputEventKind.KeyReleased, key, default, default);
    }

    public static InputEvent MousePressed(MouseButton button)
    {
        return new InputEvent(InputEventKind.MousePressed, default, button, default);
    }

    public static InputEvent MouseReleased(MouseButton button)
    {
        return new InputEvent(InputEventKind.MouseReleased, default, button, default);
    }

    public static InputEvent Text(char character)
    {
        return new InputEvent(InputEventKind.TextInput, default, default, character);
    }

    public static InputEvent Overflow()
    {
        return new InputEvent(InputEventKind.Overflow, default, default, default);
    }
}

internal sealed class InputEventRing
{
    private readonly InputEvent[] _events;
    private readonly uint _capacity;
    private readonly uint _mask;
    private uint _readPosition;
    private uint _writePosition;
    private bool _overflowPending;

    public InputEventRing(int capacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
        {
            throw new ArgumentException("Input event ring capacity must be a positive power of two.", nameof(capacity));
        }

        _events = new InputEvent[capacity];
        _capacity = (uint)capacity;
        _mask = _capacity - 1;
    }

    public bool TryEnqueue(InputEvent inputEvent)
    {
        if (_overflowPending)
        {
            if (!TryEnqueueCore(InputEvent.Overflow()))
            {
                return false;
            }

            _overflowPending = false;
        }

        if (TryEnqueueCore(inputEvent))
        {
            return true;
        }

        _overflowPending = true;
        return false;
    }

    public bool TryDequeue(out InputEvent inputEvent)
    {
        uint readPosition = _readPosition;
        if (readPosition == Volatile.Read(ref _writePosition))
        {
            inputEvent = default;
            return false;
        }

        inputEvent = _events[readPosition & _mask];
        Volatile.Write(ref _readPosition, readPosition + 1);
        return true;
    }

    public void Clear()
    {
        Volatile.Write(ref _readPosition, 0);
        Volatile.Write(ref _writePosition, 0);
        _overflowPending = false;
    }

    private bool TryEnqueueCore(InputEvent inputEvent)
    {
        uint writePosition = _writePosition;
        uint readPosition = Volatile.Read(ref _readPosition);
        if (writePosition - readPosition >= _capacity)
        {
            return false;
        }

        _events[writePosition & _mask] = inputEvent;
        Volatile.Write(ref _writePosition, writePosition + 1);
        return true;
    }
}
