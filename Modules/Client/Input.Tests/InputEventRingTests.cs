using Veldrid;
using Xunit;

namespace Karpik.Engine.Client.InputModule.Tests;

public sealed class InputEventRingTests
{
    [Fact]
    public void TryEnqueue_WhenFull_PreservesPrefixAndPublishesOverflowBeforeLaterEvents()
    {
        var ring = new InputEventRing(capacity: 2);

        Assert.True(ring.TryEnqueue(InputEvent.KeyPressed(Key.A)));
        Assert.True(ring.TryEnqueue(InputEvent.KeyPressed(Key.B)));
        Assert.False(ring.TryEnqueue(InputEvent.KeyPressed(Key.C)));

        Assert.True(ring.TryDequeue(out InputEvent first));
        Assert.True(ring.TryDequeue(out InputEvent second));
        Assert.False(ring.TryDequeue(out _));
        Assert.Equal(InputEventKind.KeyPressed, first.Kind);
        Assert.Equal(Key.A, first.Key);
        Assert.Equal(InputEventKind.KeyPressed, second.Kind);
        Assert.Equal(Key.B, second.Key);

        Assert.True(ring.TryEnqueue(InputEvent.KeyPressed(Key.D)));

        Assert.True(ring.TryDequeue(out InputEvent overflow));
        Assert.True(ring.TryDequeue(out InputEvent later));
        Assert.False(ring.TryDequeue(out _));
        Assert.Equal(InputEventKind.Overflow, overflow.Kind);
        Assert.Equal(InputEventKind.KeyPressed, later.Kind);
        Assert.Equal(Key.D, later.Key);
    }
}
