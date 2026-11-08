using System.Diagnostics;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;
using Xunit.Abstractions;

namespace Karpik.Content.Runtime.Tests;

public sealed class ContentBindingCapacityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Maximum_capacity_ready_and_changed_boundaries_allocate_nothing()
    {
        var id = new AssetId(Guid.NewGuid());
        var store = new Store();
        await using var registry = new ContentRegistry(null!, store);
        ContentManifest Manifest(string hash) => new(1,
            [new(id, "raw-json", "shared", "settings", hash, hash, 2, [])]);
        registry.RegisterManifest(Manifest("one"), store);
        await registry.LoadAsync(new AssetRef<byte[]>(id));
        await using var bindings = new ContentBindings(registry, registry, new EmptyChanges(),
            new ContentBindingOptions { Capacity = ContentBindingOptions.MaximumCapacity });
        var handles = new ContentBinding<byte[]>[ContentBindingOptions.MaximumCapacity];
        for (int i = 0; i < handles.Length; i++) handles[i] = bindings.Bind<byte[]>(id);
        for (int i = 0; i < 32; i++) bindings.AdvanceBoundary();
        foreach (var handle in handles)
            Assert.True(bindings.TryGetStatus(handle, out var status) && status.State == ContentBindingState.Ready);

        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 100; i++) bindings.AdvanceBoundary();
        long elapsed = Stopwatch.GetTimestamp() - start;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        output.WriteLine($"4096 ready bindings, mean boundary: {elapsed * 1_000_000d / Stopwatch.Frequency / 100:F1} us; allocated {allocated} bytes");

        using var next = await registry.PrepareAsync(Manifest("two"));
        Assert.True(registry.TryCommit(next));
        before = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        bindings.AdvanceBoundary();
        elapsed = Stopwatch.GetTimestamp() - start;
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        output.WriteLine($"4096 changed ready bindings: {elapsed * 1_000_000d / Stopwatch.Frequency:F1} us; allocated {allocated} bytes");
        foreach (var handle in handles)
        {
            Assert.True(bindings.TryGetStatus(handle, out var status));
            Assert.Equal(ContentBindingState.Ready, status.State);
            Assert.Equal(2u, status.PresentedVersion);
        }
        await next.Completion;
    }

    private sealed class Store : IContentStore
    {
        private readonly byte[] _bytes = "{}"u8.ToArray();
        public ReadOnlyMemory<byte> Get(string locator) => _bytes;
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Task.FromResult(Get(locator));
    }
    private sealed class EmptyChanges : IContentChangeSource
    {
        public ReadOnlySpan<ContentAssetChange> Changes => [];
        public ulong Sequence => 0;
    }
}
