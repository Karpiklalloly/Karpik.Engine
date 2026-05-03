using System.Collections.Immutable;
using System.Numerics;
using Veldrid;

namespace Karpik.Engine.Modules.Window.Core;

public interface IInputSource
{
    public InputSnapshot Snapshot { get; }
    public IReadOnlyList<char> PressedKeyChars { get; }
    public IReadOnlyList<KeyEvent> KeyEvents { get; }
    public IReadOnlyList<Key> PressedKeys { get; }
    public IReadOnlyList<MouseEvent> MouseEvents { get; }
    public ImmutableHashSet<MouseButton> PressedMouses { get; }
    public ImmutableHashSet<MouseButton> ReleasedMouses { get; }
    public Vector2 MousePosition { get; }
    public Vector2 MouseDelta { get; }
    public void Update();

    public bool IsMouseButtonDown(MouseButton button);
    public bool IsMouseButtonPressed(MouseButton button);
    public bool IsMouseButtonReleased(MouseButton button);
    public void EnableCursor();
    public void DisableCursor();
}
