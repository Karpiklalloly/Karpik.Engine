using System.Drawing;
using Karpik.Engine.Client.Graphics.Core;
using Xunit;

public sealed class ThreadBufferCommandOrderingTests
{
    [Fact]
    public void GetCommands_ReturnsCommandsOrderedBySortKeyAndStableSequence()
    {
        ThreadBuffer buffer = new();

        buffer.Add(new DrawRectCmd { Color = Color.Red, SortKey = 20 });
        buffer.Add(new DrawRectCmd { Color = Color.Green, SortKey = 10 });
        buffer.Add(new DrawRectCmd { Color = Color.Blue, SortKey = 20 });

        ReadOnlySpan<DrawCommand> commands = ((IOrderedCommandBuffer)buffer).GetCommands();

        Assert.Equal(10UL, commands[0].SortKey);
        Assert.Equal(1, commands[0].Index);
        Assert.Equal(20UL, commands[1].SortKey);
        Assert.Equal(0, commands[1].Index);
        Assert.Equal(20UL, commands[2].SortKey);
        Assert.Equal(2, commands[2].Index);
    }

    [Fact]
    public void GetCommands_ReverseOrderedStress_IsStableAndAllocationFree()
    {
        const int commandCount = 16_384;
        ThreadBuffer buffer = new();
        buffer.EnsureCapacity(rects: commandCount, textures: 0, texts: 0, commands: commandCount);

        for (int i = 0; i < commandCount; i++)
        {
            buffer.Add(new DrawRectCmd { Color = Color.Red, SortKey = (ulong)(commandCount - i) });
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        ReadOnlySpan<DrawCommand> commands = ((IOrderedCommandBuffer)buffer).GetCommands();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(0, allocatedBytes);
        for (int i = 0; i < commandCount; i++)
        {
            Assert.Equal((ulong)(i + 1), commands[i].SortKey);
            Assert.Equal(commandCount - i - 1, commands[i].Index);
        }
    }

    [Theory]
    [InlineData(int.MinValue, 0UL)]
    [InlineData(0, 2147483648UL)]
    [InlineData(int.MaxValue, 4294967295UL)]
    public void DrawSortKey_FromLayer_PreservesSignedLayerOrdering(int layer, ulong expected)
    {
        Assert.Equal(expected, DrawSortKey.FromLayer(layer));
    }

    [Fact]
    public void DrawSortKey_FromLayerDescending_PutsHigherLayersFirst()
    {
        Assert.True(DrawSortKey.FromLayerDescending(10) < DrawSortKey.FromLayerDescending(5));
    }
}
