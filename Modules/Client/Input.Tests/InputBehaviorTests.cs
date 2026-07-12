using System.Collections.Immutable;
using System.Numerics;
using Karpik.Engine.Modules.Window.Core;
using Veldrid;
using Xunit;

namespace Karpik.Engine.Client.InputModule.Tests;

public sealed class InputBehaviorTests
{
    [Fact]
    public void Update_PublishesStableStateWithoutReadingSourceFromPublicGetters()
    {
        var source = new FakeInputSource();
        var input = CreateInput(source);

        source.PressedKeysValue = [Key.A];
        source.MousePositionValue = new Vector2(10, 20);
        source.MouseDeltaValue = new Vector2(1, 2);

        input.Update();

        source.PressedKeysValue = [];
        source.MousePositionValue = new Vector2(100, 200);
        source.MouseDeltaValue = new Vector2(7, 8);

        Assert.True(input.IsDown(Key.A));
        Assert.Equal(new Vector2(10, 20), input.MousePosition);
        Assert.Equal(new Vector2(1, 2), input.MouseDelta);
    }

    [Fact]
    public void Update_PressedAndReleasedAreTransientForOneFrame()
    {
        var source = new FakeInputSource();
        var input = CreateInput(source);

        source.DownKeys = [Key.Space];
        source.PressedKeysValue = [Key.Space];
        input.Update();

        Assert.True(input.IsDown(Key.Space));
        Assert.True(input.IsPressed(Key.Space));

        source.PressedKeysValue = [];
        input.Update();

        Assert.True(input.IsDown(Key.Space));
        Assert.False(input.IsPressed(Key.Space));

        source.DownKeys = [];
        source.UnPressedKeysValue = [Key.Space];
        input.Update();

        Assert.False(input.IsDown(Key.Space));
        Assert.True(input.IsUnPressed(Key.Space));

        source.UnPressedKeysValue = [];
        input.Update();

        Assert.True(input.IsUp(Key.Space));
        Assert.False(input.IsUnPressed(Key.Space));
    }

    [Fact]
    public void Update_InvokesOfficialEventsDuringConsumePhase()
    {
        var source = new FakeInputSource();
        var input = CreateInput(source);
        Key? pressed = null;
        Key? released = null;
        char? text = null;

        input.KeyPressed += key => pressed = key;
        input.KeyReleased += key => released = key;
        input.TextInput += value => text = value;

        source.PressedKeysValue = [Key.A];
        source.UnPressedKeysValue = [Key.B];
        source.PressedKeyCharsValue = ['x'];

        input.Update();

        Assert.Equal(Key.A, pressed);
        Assert.Equal(Key.B, released);
        Assert.Equal('x', text);
    }

    [Fact]
    public void Update_PreservesLegacyEventCompatibility()
    {
        var source = new FakeInputSource();
        var input = CreateInput(source);
        Key? legacyReleased = null;
        char? legacyCharPressed = null;

        input.KeyUnPressed += key => legacyReleased = key;
        input.CharPressed += value => legacyCharPressed = value;

        source.UnPressedKeysValue = [Key.Escape];
        source.PressedKeyCharsValue = ['q'];

        input.Update();

        Assert.Equal(Key.Escape, legacyReleased);
        Assert.Equal('q', legacyCharPressed);
    }

    [Fact]
    public void CursorLock_IsAppliedOnNextUpdateAndLastRequestWins()
    {
        var source = new FakeInputSource();
        var input = CreateInput(source);

        input.LockCursor();
        input.UnlockCursor();

        Assert.False(input.IsMouseLocked);
        Assert.Equal(0, source.DisableCursorCalls);
        Assert.Equal(0, source.EnableCursorCalls);

        input.Update();

        Assert.Equal(0, source.DisableCursorCalls);
        Assert.Equal(1, source.EnableCursorCalls);
    }

    private static Input CreateInput(FakeInputSource source)
    {
        var input = new Input();
        input.Init(source, new InputCaptureState());
        return input;
    }

    private sealed class FakeInputSource : IInputSource
    {
        public IReadOnlyList<char> PressedKeyCharsValue { get; set; } = [];
        public IReadOnlyList<Key> PressedKeysValue { get; set; } = [];
        public IReadOnlyList<Key> UnPressedKeysValue { get; set; } = [];
        public IReadOnlyList<Key> DownKeys { get; set; } = [];
        public IReadOnlyList<MouseButton> PressedMouseButtons { get; set; } = [];
        public IReadOnlyList<MouseButton> ReleasedMouseButtons { get; set; } = [];
        public IReadOnlyList<MouseButton> DownMouseButtons { get; set; } = [];
        public Vector2 MousePositionValue { get; set; }
        public Vector2 MouseDeltaValue { get; set; }
        public int EnableCursorCalls { get; private set; }
        public int DisableCursorCalls { get; private set; }

        public InputSnapshot Snapshot => default!;
        public IReadOnlyList<char> PressedKeyChars => PressedKeyCharsValue;
        public IReadOnlyList<KeyEvent> KeyEvents => [];
        public IReadOnlyList<Key> PressedKeys => PressedKeysValue;
        public IReadOnlyList<Key> PressingKeys => [];
        public IReadOnlyList<Key> UnPressedKeys => UnPressedKeysValue;
        public IReadOnlyList<Key> UnPressingKeys => [];
        public IReadOnlyList<MouseEvent> MouseEvents => [];
        public ImmutableHashSet<MouseButton> PressedMouses => PressedMouseButtons.ToImmutableHashSet();
        public ImmutableHashSet<MouseButton> ReleasedMouses => ReleasedMouseButtons.ToImmutableHashSet();
        public Vector2 MousePosition => MousePositionValue;
        public Vector2 MouseDelta => MouseDeltaValue;

        public void Update()
        {
        }

        public bool IsMouseButtonDown(MouseButton button)
        {
            return DownMouseButtons.Contains(button);
        }

        public bool IsMouseButtonPressed(MouseButton button)
        {
            return PressedMouseButtons.Contains(button);
        }

        public bool IsMouseButtonReleased(MouseButton button)
        {
            return ReleasedMouseButtons.Contains(button);
        }

        public void EnableCursor()
        {
            EnableCursorCalls++;
        }

        public void DisableCursor()
        {
            DisableCursorCalls++;
        }
    }
}
