using System.Composition;
using System.Numerics;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;
using NeoVeldrid;

namespace Karpik.Engine.Modules.Window.Headless;

[Export(typeof(HeadlessInputController))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class HeadlessInputController
{
    private const int KeyCount = (int)Key.LastKey + 1;
    private const int MouseButtonCount = (int)MouseButton.LastButton + 1;

    private readonly bool[] _keyDown = new bool[KeyCount];
    private readonly bool[] _mouseDown = new bool[MouseButtonCount];
    private readonly List<Key> _pressedKeys = new(KeyCount);
    private readonly List<Key> _releasedKeys = new(KeyCount);
    private readonly List<char> _text = new(32);
    private readonly List<MouseButton> _pressedMouseButtons = new(MouseButtonCount);
    private readonly List<MouseButton> _releasedMouseButtons = new(MouseButtonCount);

    public Vector2 MousePosition { get; private set; }
    public Vector2 MouseDelta { get; private set; }
    public bool IsCursorEnabled { get; internal set; } = true;

    internal IReadOnlyList<Key> PressedKeys => _pressedKeys;
    internal IReadOnlyList<Key> ReleasedKeys => _releasedKeys;
    internal IReadOnlyList<char> Text => _text;
    internal IReadOnlyList<MouseButton> PressedMouseButtons => _pressedMouseButtons;
    internal IReadOnlyList<MouseButton> ReleasedMouseButtons => _releasedMouseButtons;

    public void PressKey(Key key)
    {
        int index = KeyIndex(key);
        if (index < 0 || _keyDown[index])
        {
            return;
        }

        _keyDown[index] = true;
        _pressedKeys.Add(key);
    }

    public void ReleaseKey(Key key)
    {
        int index = KeyIndex(key);
        if (index < 0 || !_keyDown[index])
        {
            return;
        }

        _keyDown[index] = false;
        _releasedKeys.Add(key);
    }

    public void TextInput(char value)
    {
        _text.Add(value);
    }

    public void PressMouse(MouseButton button)
    {
        int index = MouseButtonIndex(button);
        if (index < 0 || _mouseDown[index])
        {
            return;
        }

        _mouseDown[index] = true;
        _pressedMouseButtons.Add(button);
    }

    public void ReleaseMouse(MouseButton button)
    {
        int index = MouseButtonIndex(button);
        if (index < 0 || !_mouseDown[index])
        {
            return;
        }

        _mouseDown[index] = false;
        _releasedMouseButtons.Add(button);
    }

    public void MoveMouse(Vector2 position)
    {
        MouseDelta += position - MousePosition;
        MousePosition = position;
    }

    internal bool IsKeyDown(Key key)
    {
        int index = KeyIndex(key);
        return index >= 0 && _keyDown[index];
    }

    internal bool IsMouseDown(MouseButton button)
    {
        int index = MouseButtonIndex(button);
        return index >= 0 && _mouseDown[index];
    }

    internal void EndFrame()
    {
        _pressedKeys.Clear();
        _releasedKeys.Clear();
        _text.Clear();
        _pressedMouseButtons.Clear();
        _releasedMouseButtons.Clear();
        MouseDelta = Vector2.Zero;
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
}
