using System.Text;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class CapturedStoreLifetimeTests
{
    private static readonly AssetId A = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly AssetId B = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    private static ContentManifest Manifest(string a, bool includeB = true) => new(1,
        includeB ? [Entry(A, a), Entry(B, "b")] : [Entry(A, a)]);
    private static ContentManifestEntry Entry(AssetId id, string locator) => new(id, "raw", locator, "settings", locator, locator, 1, []);
    private sealed class Store(string value) : IContentStore, IContentStoreLifetime
    {
        public int References;
        public void Retain() => Interlocked.Increment(ref References);
        public void Release() => Assert.True(Interlocked.Decrement(ref References) >= 0);
        public ReadOnlyMemory<byte> Get(string locator) => Encoding.UTF8.GetBytes(value);
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Task.FromResult(Get(locator));
    }

    [Fact]
    public async Task ChangedSlotsUseCandidateStore_UnchangedUnloadedSlotsKeepCapturedStore()
    {
        var old = new Store("o");
        var candidate = new Store("n");
        await using var registry = new ContentRegistry(null!, old);
        registry.RegisterManifest(Manifest("a"), old);
        using var prepared = await registry.PrepareAsync(Manifest("c"), candidate);
        Assert.True(registry.TryCommit(prepared));
        await prepared.Completion;
        Assert.True(registry.TryResolve<string>(A, out var changed));
        Assert.True(registry.TryResolve<string>(B, out var unchanged));
        await registry.LoadAsync(changed);
        await registry.LoadAsync(unchanged);
        Assert.Equal("n", Payload(registry, changed));
        Assert.Equal("o", Payload(registry, unchanged));
        Assert.Equal(1u, unchanged.Version);
        Assert.True(old.References > 0);
        using var removal = await registry.PrepareAsync(Manifest("d", false), candidate);
        Assert.True(registry.TryCommit(removal));
        await removal.Completion;
        Assert.Equal(0, old.References);
        await registry.DisposeAsync();
        Assert.Equal(0, candidate.References);
    }

    private sealed class GatedStore : IContentStore, IContentStoreLifetime
    {
        public readonly TaskCompletionSource<ReadOnlyMemory<byte>> Read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int References;
        public void Retain() => Interlocked.Increment(ref References);
        public void Release() => Assert.True(Interlocked.Decrement(ref References) >= 0);
        public ReadOnlyMemory<byte> Get(string locator) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Read.Task;
    }

    [Fact]
    public async Task ReplacedStoreRemainsOwnedUntilInflightReadSettles()
    {
        var old = new GatedStore();
        var candidate = new Store("n");
        await using var registry = new ContentRegistry(null!, old);
        registry.RegisterManifest(Manifest("a", false), old);
        Task load = registry.LoadAsync(new AssetRef<string>(A));
        using var prepared = await registry.PrepareAsync(Manifest("c", false), candidate);
        Assert.True(registry.TryCommit(prepared));
        Assert.False(prepared.Completion.IsCompleted);
        Assert.True(old.References > 0);
        old.Read.SetResult(Encoding.UTF8.GetBytes("o"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => load);
        await prepared.Completion;
        Assert.Equal(0, old.References);
    }

    [Fact]
    public async Task AbortingCandidateReleasesOnlyItsCapturedStore()
    {
        var old = new Store("o");
        var candidate = new Store("n");
        await using var registry = new ContentRegistry(null!, old);
        registry.RegisterManifest(Manifest("a", false), old);
        var prepared = await registry.PrepareAsync(Manifest("c", false), candidate);
        Assert.True(candidate.References > 0);
        prepared.Dispose();
        await prepared.Completion;
        Assert.Equal(0, candidate.References);
        Assert.True(old.References > 0);
        await registry.DisposeAsync();
        Assert.Equal(0, old.References);
    }

    private static string Payload(ContentRegistry registry, AssetRef<string> reference)
    {
        Assert.True(registry.TryGet(reference, out var lease));
        return lease.Payload;
    }
}
