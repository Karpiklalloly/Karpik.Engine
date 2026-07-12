using System.Collections.Immutable;
using System.Numerics;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;

namespace Karpik.Engine.Modules.Window.Headless;

public sealed class HeadlessInputSource : IInputSource
{
    private readonly HeadlessInputController _controller;
    private readonly List<Key> _pressedKeys = [];
    private readonly List<Key> _releasedKeys = [];
    private readonly List<char> _text = [];
    private readonly List<MouseButton> _pressedMouseButtons = [];
    private readonly List<MouseButton> _releasedMouseButtons = [];
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;

    public HeadlessInputSource(HeadlessInputController controller)
    {
        _controller = controller;
    }

    public InputSnapshot Snapshot => default!;
    public IReadOnlyList<char> PressedKeyChars => _text;
    public IReadOnlyList<KeyEvent> KeyEvents => [];
    public IReadOnlyList<Key> PressedKeys => _pressedKeys;
    public IReadOnlyList<Key> PressingKeys => [];
    public IReadOnlyList<Key> UnPressedKeys => _releasedKeys;
    public IReadOnlyList<Key> UnPressingKeys => [];
    public IReadOnlyList<MouseEvent> MouseEvents => [];
    public ImmutableHashSet<MouseButton> PressedMouses => _pressedMouseButtons.ToImmutableHashSet();
    public ImmutableHashSet<MouseButton> ReleasedMouses => _releasedMouseButtons.ToImmutableHashSet();
    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;

    public void Update()
    {
        _pressedKeys.Clear();
        _releasedKeys.Clear();
        _text.Clear();
        _pressedMouseButtons.Clear();
        _releasedMouseButtons.Clear();

        Copy(_controller.PressedKeys, _pressedKeys);
        Copy(_controller.ReleasedKeys, _releasedKeys);
        Copy(_controller.Text, _text);
        Copy(_controller.PressedMouseButtons, _pressedMouseButtons);
        Copy(_controller.ReleasedMouseButtons, _releasedMouseButtons);
        _mousePosition = _controller.MousePosition;
        _mouseDelta = _controller.MouseDelta;
        _controller.EndFrame();
    }

    public bool IsMouseButtonDown(MouseButton button)
    {
        return _controller.IsMouseDown(button);
    }

    public bool IsMouseButtonPressed(MouseButton button)
    {
        return _pressedMouseButtons.Contains(button);
    }

    public bool IsMouseButtonReleased(MouseButton button)
    {
        return _releasedMouseButtons.Contains(button);
    }

    public void EnableCursor()
    {
        _controller.IsCursorEnabled = true;
    }

    public void DisableCursor()
    {
        _controller.IsCursorEnabled = false;
    }

    private static void Copy<T>(IReadOnlyList<T> source, List<T> target)
    {
        for (int i = 0; i < source.Count; i++)
        {
            target.Add(source[i]);
        }
    }
}
