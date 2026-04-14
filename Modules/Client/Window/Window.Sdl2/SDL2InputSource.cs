using System.Collections.Immutable;
using System.Numerics;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;
using Veldrid.Sdl2;

namespace Karpik.Engine.Modules.Window.Sdl2;

public class SDL2InputSource : IInputSource
{
    private readonly Sdl2Window _window;
    private InputSnapshot _snapshot;

    public IReadOnlyList<char> PressedKeyChars => _snapshot.KeyCharPresses;
    public IReadOnlyList<KeyEvent> KeyEvents => _snapshot.KeyEvents;
    public IReadOnlyList<Key> PressedKeys { get; private set; }
    public IReadOnlyList<MouseEvent> MouseEvents => _snapshot.MouseEvents;
    public ImmutableHashSet<MouseButton> PressedMouses { get; private set; }
    public ImmutableHashSet<MouseButton> ReleasedMouses { get; private set; }
    public Vector2 MousePosition => _snapshot.MousePosition;
    public Vector2 MouseDelta => _window.MouseDelta; 

    public SDL2InputSource(Sdl2Window window)
    {
        _window = window;
    }

    public void Update()
    {
        // TODO: Аллокации каждый кадр - кринж. Пул надо использовать
        _snapshot = _window.PumpEvents();
        PressedKeys = KeyEvents.Where(static x => x.Down).Select(static x => x.Key).ToList();
        PressedMouses = _snapshot.MouseEvents.Where(static x => x.Down).Select(static x => x.MouseButton).ToImmutableHashSet();
        ReleasedMouses = _snapshot.MouseEvents.Where(static x => !x.Down).Select(static x => x.MouseButton).ToImmutableHashSet();
        
    }

    public bool IsMouseButtonDown(MouseButton button)
    {
        return _snapshot.IsMouseDown(button);
    }
    
    public bool IsMouseButtonPressed(MouseButton button)
    {
        return PressedMouses.Contains(button);
    }

    public bool IsMouseButtonReleased(MouseButton button)
    {
        return ReleasedMouses.Contains(button);
    }

    public void EnableCursor()
    {
        _window.CursorVisible = true;
    }

    public void DisableCursor()
    {
        // TODO: Сделать надо не выключить, а лочить в центре курсор
        _window.CursorVisible = false;
    }
}