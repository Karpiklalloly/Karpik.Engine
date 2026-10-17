using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class AllocationTests
{
    [Fact]
    public void AssetRef_Copy_ZeroAlloc()
    {
        var r = new AssetRef<Karpik.Content.Runtime.RawJsonPayload>(new AssetId(Guid.NewGuid()), 1);
        // Warmup JIT without per-iter boxing: use sink pattern, touch blittable fields
        AssetRef<Karpik.Content.Runtime.RawJsonPayload> warmSink = default;
        for (int i = 0; i < 10; i++)
        {
            warmSink = r;
            _ = warmSink.Id;
            _ = warmSink.Version;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        AssetRef<Karpik.Content.Runtime.RawJsonPayload> sink = default;
        for (int i = 0; i < 100_000; i++)
        {
            sink = r;
            // Touch blittable fields to prevent dead-store elimination without heap access
            _ = sink.Version;
            _ = sink.Id;
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(sink);
        GC.KeepAlive(r);
        GC.KeepAlive(warmSink);
        Assert.Equal(0, after - before);
    }
}
