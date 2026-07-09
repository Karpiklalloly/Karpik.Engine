using System.Drawing;
using Karpik.Engine.Client.Graphics.Core;
using Xunit;

public sealed class GraphicsContextCommandSetTests
{
    [Fact]
    public void Buffer_UsesThreeDistinctThreadBuffersBeforeReusingSlot()
    {
        GraphicsContext.BeginFrame();
        ICommandBuffer first = GraphicsContext.Buffer;
        first.Add(new DrawRectCmd { Color = Color.Red });

        GraphicsContext.BeginFrame();
        ICommandBuffer second = GraphicsContext.Buffer;
        second.Add(new DrawRectCmd { Color = Color.Green });

        GraphicsContext.BeginFrame();
        ICommandBuffer third = GraphicsContext.Buffer;
        third.Add(new DrawRectCmd { Color = Color.Blue });

        Assert.NotSame(first, second);
        Assert.NotSame(second, third);
        Assert.NotSame(first, third);

        GraphicsContext.BeginFrame();
        ICommandBuffer fourth = GraphicsContext.Buffer;

        Assert.Same(first, fourth);
    }

    [Fact]
    public void CollectBuffers_ReturnsPreviousWriteSet()
    {
        GraphicsContext.BeginFrame();
        ICommandBuffer written = GraphicsContext.Buffer;
        written.Add(new DrawRectCmd { Color = Color.Red });

        GraphicsContext.BeginFrame();
        List<ICommandBuffer> pending = GraphicsContext.CollectBuffers();

        Assert.Contains(written, pending);
    }
}
