using System.Drawing;
using Karpik.Engine.Client.Graphics.Core;
using Xunit;

public sealed class GraphicsContextCommandSetTests
{
    [Fact]
    public void Buffer_DoesNotReuseCommandSetWhileMergeWorkerOwnsIt()
    {
        GraphicsContext.BeginFrame();
        ICommandBuffer written = GraphicsContext.Buffer;
        written.Add(new DrawRectCmd { Color = Color.Red });

        GraphicsContext.BeginFrame();
        Assert.True(GraphicsContext.TryAcquireMergeBuffers(out int commandSetIndex, out List<ICommandBuffer>? pending));
        Assert.Contains(written, pending!);

        GraphicsContext.BeginFrame();
        ICommandBuffer next = GraphicsContext.Buffer;

        Assert.NotSame(written, next);

        GraphicsContext.ReleaseMergeBuffers(commandSetIndex);
    }

    [Fact]
    public void TryAcquireMergeBuffers_ReturnsPreviousWriteSet()
    {
        GraphicsContext.BeginFrame();
        ICommandBuffer written = GraphicsContext.Buffer;
        written.Add(new DrawRectCmd { Color = Color.Red });

        GraphicsContext.BeginFrame();
        Assert.True(GraphicsContext.TryAcquireMergeBuffers(out int commandSetIndex, out List<ICommandBuffer>? pending));

        Assert.Contains(written, pending!);
        GraphicsContext.ReleaseMergeBuffers(commandSetIndex);
    }
}
