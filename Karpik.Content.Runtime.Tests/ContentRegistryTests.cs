using System.Text;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class ContentRegistryTests
{
    private sealed class CountingStore : IContentStore
    {
        public int Calls;
        private readonly int _delayMs;
        private readonly ReadOnlyMemory<byte> _bytes;

        public CountingStore(int delayMs = 50, string payload = "{}")
        {
            _delayMs = delayMs;
            _bytes = Encoding.UTF8.GetBytes(payload);
        }

        public ReadOnlyMemory<byte> Get(string artifactLocator)
        {
            Interlocked.Increment(ref Calls);
            return _bytes;
        }

        public async Task<ReadOnlyMemory<byte>> GetAsync(string artifactLocator, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            if (_delayMs > 0)
                await Task.Delay(_delayMs, ct);
            return _bytes;
        }
    }

    private sealed class FailingStore : IContentStore
    {
        public int Calls;
        private readonly ReadOnlyMemory<byte> _bytes;
        private readonly bool _failFirst;

        public FailingStore(bool failFirst = true, string payload = "{}")
        {
            _failFirst = failFirst;
            _bytes = Encoding.UTF8.GetBytes(payload);
        }

        public ReadOnlyMemory<byte> Get(string artifactLocator)
        {
            Interlocked.Increment(ref Calls);
            if (_failFirst && Calls == 1)
                throw new InvalidDataException("Missing artifact " + artifactLocator);
            return _bytes;
        }

        public Task<ReadOnlyMemory<byte>> GetAsync(string artifactLocator, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            if (_failFirst && Calls == 1)
                return Task.FromException<ReadOnlyMemory<byte>>(new InvalidDataException("Missing artifact " + artifactLocator));
            return Task.FromResult(_bytes);
        }
    }

    private sealed class FlakyStore : IContentStore
    {
        public int Calls;
        private readonly ReadOnlyMemory<byte> _bytes;
        private int _failCount;

        public FlakyStore(int failCount, string payload = "{}")
        {
            _failCount = failCount;
            _bytes = Encoding.UTF8.GetBytes(payload);
        }

        public ReadOnlyMemory<byte> Get(string artifactLocator)
        {
            var c = Interlocked.Increment(ref Calls);
            if (c <= _failCount)
                throw new InvalidDataException("Missing artifact " + artifactLocator);
            return _bytes;
        }

        public Task<ReadOnlyMemory<byte>> GetAsync(string artifactLocator, CancellationToken ct = default)
        {
            var c = Interlocked.Increment(ref Calls);
            if (c <= _failCount)
                return Task.FromException<ReadOnlyMemory<byte>>(new InvalidDataException("Missing artifact " + artifactLocator));
            return Task.FromResult(_bytes);
        }
    }

    private static ContentManifest MakeManifest(params (AssetId id, string logical, string locator)[] entries)
    {
        var list = new List<ContentManifestEntry>();
        foreach (var (id, logical, locator) in entries)
        {
            list.Add(new ContentManifestEntry(
                id,
                "raw-json",
                logical,
                "hash-import",
                "hash-source",
                locator,
                2,
                Array.Empty<AssetId>()));
        }
        return new ContentManifest(ContentManifest.CurrentSchemaVersion, list);
    }

    [Fact]
    public async Task SingleFlight_TwoConcurrentLoads_OneStoreGet()
    {
        var idA = new AssetId(Guid.NewGuid());
        var idB = new AssetId(Guid.NewGuid());
        var manifest = MakeManifest((idA, "game/a", "artifacts/ab/cd/hash1.cooked"), (idB, "game/b", "artifacts/ef/gh/hash2.cooked"));
        var store = new CountingStore(delayMs: 80);
        var registry = new ContentRegistry();
        registry.RegisterManifest(manifest, store);

        var r = new AssetRef<RawJsonPayload>(idA, "game/a", 1);
        var t1 = registry.LoadAsync<RawJsonPayload>(r);
        var t2 = registry.LoadAsync<RawJsonPayload>(r);

        await Task.WhenAll(t1, t2);

        Assert.Equal(1, store.Calls);
        // After load, TryGet should succeed and IsAlive true
        Assert.True(registry.IsAlive(r));
        Assert.True(registry.TryGet(r, out var lease));
        Assert.Equal(r.Version, 1u);
    }

    [Fact]
    public void TryGet_Missing_ReturnsFalse()
    {
        var idA = new AssetId(Guid.NewGuid());
        var manifest = MakeManifest((idA, "game/a", "artifacts/ab/cd/hash1.cooked"));
        var store = new CountingStore(delayMs: 0);
        var registry = new ContentRegistry();
        registry.RegisterManifest(manifest, store);

        var missingId = new AssetId(Guid.NewGuid());
        var missingRef = new AssetRef<RawJsonPayload>(missingId, "game/missing", 1);
        Assert.False(registry.TryGet(missingRef, out var lease));
        Assert.False(registry.IsAlive(missingRef));

        // Also TryGet before load should be false even for known id
        var knownRef = new AssetRef<RawJsonPayload>(idA, "game/a", 1);
        Assert.False(registry.TryGet(knownRef, out _));
        Assert.False(registry.IsAlive(knownRef));
    }

    [Fact]
    public async Task Lease_VersionMismatch_IsAliveFalse()
    {
        var idA = new AssetId(Guid.NewGuid());
        var manifest = MakeManifest((idA, "game/a", "artifacts/ab/cd/hash1.cooked"));
        var store = new CountingStore(delayMs: 0);
        var registry = new ContentRegistry();
        registry.RegisterManifest(manifest, store);

        var r = new AssetRef<RawJsonPayload>(idA, "game/a", 1);
        await registry.LoadAsync<RawJsonPayload>(r);

        Assert.True(registry.TryGet(r, out var lease));
        Assert.True(lease.IsAlive);
        Assert.True(registry.IsAlive(r));

        // Create ref with mismatched version
        var wrongVersionRef = new AssetRef<RawJsonPayload>(idA, "game/a", 999);
        Assert.False(registry.IsAlive(wrongVersionRef));
        Assert.False(registry.TryGet(wrongVersionRef, out _));
        // Also check IsAlive via non-generic helper
        Assert.False(registry.IsAlive(idA, 999));
        Assert.True(registry.IsAlive(idA, 1));
    }

    [Fact]
    public async Task LoadAsync_Success_TransitionsToLoaded()
    {
        var idA = new AssetId(Guid.NewGuid());
        var manifest = MakeManifest((idA, "game/a", "artifacts/ab/cd/hash1.cooked"));
        var store = new CountingStore(delayMs: 10);
        var registry = new ContentRegistry();
        registry.RegisterManifest(manifest, store);

        var r = new AssetRef<RawJsonPayload>(idA, "game/a", 1);
        Assert.False(registry.IsAlive(r));
        await registry.LoadAsync<RawJsonPayload>(r);
        Assert.True(registry.IsAlive(r));
        Assert.True(registry.TryGet(r, out var lease));
        Assert.True(lease.IsAlive);
        Assert.NotNull(lease.Payload);
    }

    [Fact]
    public async Task Failed_Retryable_SecondLoadRetries()
    {
        var idA = new AssetId(Guid.NewGuid());
        var manifest = MakeManifest((idA, "game/a", "artifacts/ab/cd/missing.cooked"));
        var flaky = new FlakyStore(failCount: 1);
        var registry = new ContentRegistry();
        registry.RegisterManifest(manifest, flaky);

        var r = new AssetRef<RawJsonPayload>(idA, "game/a", 1);

        // First load should fail
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.LoadAsync<RawJsonPayload>(r));
        Assert.False(registry.IsAlive(r));
        Assert.False(registry.TryGet(r, out _));
        Assert.Equal(1, flaky.Calls);

        // Second load should retry and succeed
        await registry.LoadAsync<RawJsonPayload>(r);
        Assert.Equal(2, flaky.Calls);
        Assert.True(registry.IsAlive(r));
        Assert.True(registry.TryGet(r, out var lease));
    }

    [Fact]
    public async Task DuplicateLoad_AfterLoaded_NoExtraStoreCall()
    {
        var idA = new AssetId(Guid.NewGuid());
        var manifest = MakeManifest((idA, "game/a", "artifacts/ab/cd/hash1.cooked"));
        var store = new CountingStore(delayMs: 10);
        var registry = new ContentRegistry();
        registry.RegisterManifest(manifest, store);

        var r = new AssetRef<RawJsonPayload>(idA, "game/a", 1);
        await registry.LoadAsync<RawJsonPayload>(r);
        Assert.Equal(1, store.Calls);

        // Second load after already Loaded should not hit store again (fast path)
        await registry.LoadAsync<RawJsonPayload>(r);
        Assert.Equal(1, store.Calls);
    }
}
