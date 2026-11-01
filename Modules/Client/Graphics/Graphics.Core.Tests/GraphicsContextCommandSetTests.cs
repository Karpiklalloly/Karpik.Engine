using System.Drawing;
using Karpik.Engine.Client.Graphics.Core;
using Xunit;

public sealed class GraphicsContextCommandSetTests
{
    private sealed class Texture(Karpik.Content.Runtime.ContentResourceLifetime owner) : ITexture2D, Karpik.Content.Runtime.IContentTrackedResource
    {
        public uint Width => 1; public uint Height => 1;
        public Karpik.Content.Runtime.ContentResourceLifetime Lifetime => owner;
        public void Dispose() => owner.Retire();
    }
    [Fact]
    public void DroppedReadyFrameAndShutdownReleaseTextureClaims()
    {
        var first = new Karpik.Content.Runtime.ContentResourceLifetime(() => { });
        GraphicsContext.BeginFrame();
        GraphicsContext.Buffer.Add(new DrawTextureCmd { Texture = new Texture(first) });
        GraphicsContext.BeginFrame();
        first.Retire();
        Assert.False(first.TryDispose());
        GraphicsContext.BeginFrame();
        Assert.True(first.TryDispose());
        var second = new Karpik.Content.Runtime.ContentResourceLifetime(() => { });
        GraphicsContext.Buffer.Add(new DrawTextureCmd { Texture = new Texture(second) });
        second.Retire();
        GraphicsContext.DiscardPendingFrames();
        Assert.True(second.TryDispose());
    }

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
