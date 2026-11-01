using Karpik.Content.Runtime;
using Xunit;
namespace Karpik.Engine.Client.Graphics.Core.Tests;
public class ResourceOwnershipTests
{
    private sealed class Texture(ContentResourceLifetime lifetime) : ITexture2D, IContentTrackedResource
    {
        public uint Width => 1;
        public uint Height => 1;
        public ContentResourceLifetime Lifetime => lifetime;
        public void Dispose() => lifetime.Retire();
    }
    private sealed class Disposable(Action action) : IDisposable { public void Dispose() => action(); }
    [Fact]
    public void ShutdownReleasesCpuClaimsAndAttemptsAllNativeResourcesAfterFailure()
    {
        var owner = new ContentResourceLifetime(() => { });
        var pins = new ContentResourcePins(2); pins.TryAdd(owner); owner.Retire();
        int nativeDisposals = 0;
        Assert.Throws<AggregateException>(() => GraphicsResourceShutdown.Dispose(
            () => throw new InvalidOperationException("wait"), pins.Clear,
            new Disposable(() => { nativeDisposals++; throw new InvalidOperationException("dispose"); }),
            new Disposable(() => nativeDisposals++)));
        Assert.Equal(2, nativeDisposals);
        Assert.True(owner.TryDispose());
    }

    [Fact]
    public void RecordedTextureSurvivesRetirement_TransferAndDropReleaseExactlyOnce()
    {
        int disposed = 0;
        var owner = new ContentResourceLifetime(() => disposed++);
        var texture = new Texture(owner);
        var source = new ThreadBuffer();
        var command = new DrawTextureCmd { Texture = texture };
        source.Add(command);
        source.Add(command);
        owner.Retire();
        Assert.False(owner.TryDispose());
        var submitted = new ContentResourcePins(4);
        source.RetainResources(submitted);
        source.Clear();
        Assert.False(owner.TryDispose());
        submitted.Clear();
        Assert.True(owner.TryDispose());
        Assert.Equal(1, disposed);
        source.Add(command);
        Assert.Empty(source.GetTextureCommands().ToArray());
    }
    [Fact]
    public void PinningAndClearingRecordedResources_AllocateNothingAfterWarmup()
    {
        var texture = new Texture(new ContentResourceLifetime(() => { }));
        var buffer = new ThreadBuffer();
        var command = new DrawTextureCmd { Texture = texture };
        for (int i = 0; i < 100; i++) { buffer.Add(command); buffer.Clear(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { buffer.Add(command); buffer.Clear(); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
