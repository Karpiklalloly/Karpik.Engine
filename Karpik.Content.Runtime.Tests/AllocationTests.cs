using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class AllocationTests
{
    [Fact]
    public void AssetRef_Copy_ZeroAlloc()
    {
        var r = new AssetRef<RawJsonPayload>(new AssetId(Guid.NewGuid()), "game/a");
        // Warmup JIT to avoid one-time alloc counted in measurement
        for (int i = 0; i < 10; i++)
        {
            var tmp = r;
            GC.KeepAlive(tmp);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        AssetRef<RawJsonPayload> sink = default;
        for (int i = 0; i < 100_000; i++)
        {
            sink = r;
            // Touch fields to prevent dead-store elimination without boxing
            _ = sink.Version;
            _ = sink.Id;
            _ = sink.LogicalName.Length;
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(sink);
        GC.KeepAlive(r);
        Assert.Equal(0, after - before);
    }
}
