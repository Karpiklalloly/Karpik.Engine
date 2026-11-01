using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public class ContentUpdateTests
{
    private static readonly AssetId Id = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static ContentManifest Manifest(string locator = "a") => new(1,
        [new(Id, "raw", "name", "settings", locator, locator, 1, [])]);
    private sealed class Store : IContentStore
    {
        public ReadOnlyMemory<byte> Get(string locator) => System.Text.Encoding.UTF8.GetBytes(locator);
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Task.FromResult(Get(locator));
    }
    private static ContentRegistry Registry(Store store)
    {
        var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(Manifest(), store);
        return registry;
    }
    [Fact]
    public async Task PrepareIsInvisible_CommitInvalidatesOnlyChangedGeneration()
    {
        var registry = Registry(new Store());
        var original = new AssetRef<string>(Id);
        await registry.LoadAsync(original);
        using var prepared = await registry.PrepareAsync(Manifest("b"));
        Assert.True(registry.TryGet(original, out var before));
        Assert.Equal("a", before.Payload);
        Assert.True(registry.TryCommit(prepared));
        Assert.False(registry.TryGet(original, out _));
        Assert.True(registry.TryResolve<string>(Id, out var current));
        Assert.Equal(2u, current.Version);
        Assert.True(registry.TryGet(current, out var after));
        Assert.Equal("b", after.Payload);
        Assert.False(registry.TryCommit(prepared));
        await prepared.Completion;
    }
    [Fact]
    public async Task RemoveThenReadd_DoesNotReviveOldReference()
    {
        var registry = Registry(new Store());
        using (var removal = await registry.PrepareAsync(new ContentManifest(1, [])))
        {
            Assert.True(registry.TryCommit(removal));
            await removal.Completion;
        }
        using var addition = await registry.PrepareAsync(Manifest());
        Assert.True(registry.TryCommit(addition));
        Assert.True(registry.TryResolve<string>(Id, out var current));
        Assert.Equal(3u, current.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.LoadAsync(new AssetRef<string>(Id)));
        await addition.Completion;
    }
    [Fact]
    public async Task UnchangedGenerationRetainsPayload_AndInitializationCannotResetIt()
    {
        var store = new Store();
        var registry = Registry(store);
        await registry.LoadAsync(new AssetRef<string>(Id));
        using var candidate = await registry.PrepareAsync(Manifest());
        Assert.True(registry.TryCommit(candidate));
        Assert.True(registry.TryResolve<string>(Id, out var current));
        Assert.Equal(1u, current.Version);
        Assert.True(registry.TryGet(current, out _));
        Assert.Throws<InvalidOperationException>(() => registry.RegisterManifest(Manifest(), store));
        await candidate.Completion;
    }
    [Fact]
    public async Task InvalidJsonAndDeclaredTypeMigration_RejectPreparation()
    {
        var store = new Store();
        var registry = Registry(store);
        await registry.LoadAsync(new AssetRef<RawJsonPayload>(Id));
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => registry.PrepareAsync(Manifest("b")));
        var migration = new ContentManifest(1, [new(Id, "different", "name", "settings", "x", "x", 1, [])]);
        await Assert.ThrowsAsync<NotSupportedException>(() => registry.PrepareAsync(migration));
        Assert.True(registry.IsAlive(new AssetRef<RawJsonPayload>(Id)));
    }
    [Fact]
    public async Task ResolveRejectsWrongBoundType_AndNoOpDoesNotConsumeRevision()
    {
        var registry = Registry(new Store());
        await registry.LoadAsync(new AssetRef<string>(Id));
        Assert.False(registry.TryResolve<byte[]>(Id, out _));
        using (var noOp = await registry.PrepareAsync(Manifest()))
        {
            Assert.True(registry.TryCommit(noOp));
            await noOp.Completion;
        }
        using var update = await registry.PrepareAsync(Manifest("b"));
        Assert.True(registry.TryCommit(update));
        Assert.True(registry.TryResolve<string>(Id, out var reference));
        Assert.Equal(2u, reference.Version);
        await update.Completion;
    }
    [Fact]
    public async Task MissingDependencyAndCycle_AreRejectedBeforePublication()
    {
        var registry = Registry(new Store());
        var missing = new AssetId(Guid.NewGuid());
        var manifest = new ContentManifest(1, [new(Id, "raw", "name", "s", "s", "a", 1, [missing])]);
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.PrepareAsync(manifest));
        manifest = new ContentManifest(1, [new(Id, "raw", "name", "s", "s", "a", 1, [Id])]);
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.PrepareAsync(manifest));
    }
    private sealed class GatedStore : IContentStore
    {
        public readonly TaskCompletionSource<ReadOnlyMemory<byte>> Old = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ReadOnlyMemory<byte> Get(string locator) => throw new NotSupportedException();
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => locator == "a" ? Old.Task : Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 98 });
    }
    [Fact]
    public async Task LateOldLoadCannotPublishIntoReplacement_AndMixedTypesAreRejected()
    {
        var store = new GatedStore();
        var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(Manifest(), store);
        var oldLoad = registry.LoadAsync(new AssetRef<string>(Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.LoadAsync(new AssetRef<byte[]>(Id)));
        using var update = await registry.PrepareAsync(Manifest("b"));
        Assert.True(registry.TryCommit(update));
        Assert.True(registry.TryResolve<string>(Id, out var current));
        await registry.LoadAsync(current);
        store.Old.SetResult(new byte[] { 97 });
        try { await oldLoad; } catch (InvalidOperationException) { }
        await update.Completion;
        Assert.True(registry.TryGet(current, out var lease));
        Assert.Equal("b", lease.Payload);
        Assert.False(registry.IsAlive(new AssetRef<string>(Id)));
    }
    [Fact]
    public async Task AbortAndForeignCommitCannotPublish_AndPreparationIsBounded()
    {
        var registry = Registry(new Store());
        var other = Registry(new Store());
        using var update = await registry.PrepareAsync(Manifest("b"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.PrepareAsync(Manifest("c")));
        Assert.False(other.TryCommit(update));
        update.Dispose();
        await update.Completion;
        Assert.False(registry.TryCommit(update));
        Assert.True(registry.TryResolve<string>(Id, out var original));
        Assert.Equal(1u, original.Version);
        using var next = await registry.PrepareAsync(Manifest("c"));
        next.Dispose(); await next.Completion;
    }
    [Fact]
    public async Task CancellationAndSizeMismatchPreserveLastGoodPayload()
    {
        var registry = Registry(new Store());
        var original = new AssetRef<string>(Id);
        await registry.LoadAsync(original);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.PrepareAsync(Manifest("b"), cancellation.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.PrepareAsync(Manifest("too-long")));
        Assert.True(registry.TryGet(original, out var lease));
        Assert.Equal("a", lease.Payload);
    }
    [Fact]
    public async Task PublicationAndReadsAllocateNothingAfterWarmup()
    {
        var registry = Registry(new Store());
        await registry.LoadAsync(new AssetRef<string>(Id));
        using (var warm = await registry.PrepareAsync(Manifest())) { registry.TryCommit(warm); await warm.Completion; }
        using var update = await registry.PrepareAsync(Manifest("b"));
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool committed = registry.TryCommit(update);
        for (int i = 0; i < 1000; i++)
        {
            registry.TryResolve<string>(Id, out var reference);
            registry.TryGet(reference, out _);
            registry.IsAlive(reference);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(committed);
        Assert.Equal(0, allocated);
        await update.Completion;
    }
    [Fact]
    public async Task ShutdownInvalidatesRootAndRejectsFurtherLoads()
    {
        var registry = Registry(new Store());
        await registry.LoadAsync(new AssetRef<string>(Id));
        await registry.DisposeAsync();
        Assert.False(registry.IsAlive(new AssetRef<string>(Id)));
        Assert.False(registry.TryResolve<string>(Id, out _));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => registry.LoadAsync(new AssetRef<string>(Id)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => registry.PrepareAsync(Manifest()));
    }
    private sealed class Payload(ContentResourceLifetime owner) : IContentTrackedResource
    {
        public ContentResourceLifetime Lifetime => owner;
    }
    private sealed class Loader : IContentPayloadLoader
    {
        public Func<object> Create = () => throw new NotImplementedException();
        public bool CanLoad(Type type, string declared) => type == typeof(Payload);
        public ValueTask<object> LoadAsync(Type type, string declared, ReadOnlyMemory<byte> bytes, CancellationToken ct) => ValueTask.FromResult(Create());
    }
    [Fact]
    public async Task CleanupFailureCompletesPublicLoadInsteadOfStrandingInflight()
    {
        using var cancellation = new CancellationTokenSource();
        var loader = new Loader { Create = () => { cancellation.Cancel(); return new Payload(new ContentResourceLifetime(() => throw new InvalidOperationException("destroy"))); } };
        var store = new Store();
        var registry = new ContentRegistry(null!, store, [loader]);
        registry.RegisterManifest(Manifest(), store);
        var task = registry.LoadAsync(new AssetRef<Payload>(Id), cancellation.Token);
        await Assert.ThrowsAsync<AggregateException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
        loader.Create = () => new Payload(new ContentResourceLifetime(() => { }));
        await registry.LoadAsync(new AssetRef<Payload>(Id)).WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<AggregateException>(() => registry.DisposeAsync().AsTask());
    }
    [Fact]
    public async Task RetirementAttemptsEveryResourceDespiteDestructionFailure()
    {
        int destroyed = 0;
        var loader = new Loader { Create = () => new Payload(new ContentResourceLifetime(() =>
        {
            if (Interlocked.Increment(ref destroyed) == 1) throw new InvalidOperationException("destroy");
        })) };
        var store = new Store();
        var second = new AssetId(Guid.NewGuid());
        var registry = new ContentRegistry(null!, store, [loader]);
        registry.RegisterManifest(new(1, [Manifest().Entries[0], new(second, "raw", "two", "s", "s", "b", 1, [])]), store);
        await registry.LoadAsync(new AssetRef<Payload>(Id)); await registry.LoadAsync(new AssetRef<Payload>(second));
        using var update = await registry.PrepareAsync(new(1, []));
        registry.TryCommit(update);
        await Assert.ThrowsAsync<AggregateException>(() => update.Completion);
        Assert.Equal(2, destroyed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.PrepareAsync(Manifest()));
    }
    [Fact]
    public async Task FailedPreparationCleanupPoisonsSubsequentUpdates()
    {
        int calls = 0;
        var loader = new Loader { Create = () =>
        {
            if (++calls == 4) throw new InvalidDataException("decode");
            return new Payload(new ContentResourceLifetime(calls == 3 ? () => throw new InvalidOperationException("destroy") : () => { }));
        } };
        var store = new Store(); var second = new AssetId(Guid.NewGuid());
        ContentManifest ManifestPair(string prefix) => new(1, [new(Id, "raw", "one", "s", prefix, "a", 1, []), new(second, "raw", "two", "s", prefix, "b", 1, [])]);
        var registry = new ContentRegistry(null!, store, [loader]); registry.RegisterManifest(ManifestPair("old"), store);
        await registry.LoadAsync(new AssetRef<Payload>(Id)); await registry.LoadAsync(new AssetRef<Payload>(second));
        await Assert.ThrowsAsync<AggregateException>(() => registry.PrepareAsync(ManifestPair("new")));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var candidate = await registry.PrepareAsync(new(1, [])); });
        await registry.DisposeAsync();
    }
    [Fact]
    public void RegistryHasOneAccessibleConstructorForStaticComposition()
        => Assert.Single(typeof(ContentRegistry).GetConstructors());

    [Fact]
    public async Task RepeatedDrainAfterDisposalIsIdempotent()
    {
        int destroyed = 0;
        var owner = new ContentResourceLifetime(() => destroyed++);
        await owner.DrainAsync();
        await owner.DrainAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, destroyed);
    }

    [Fact]
    public async Task LifetimeRetainsUntilLastCpuOwner_AndDisposesExactlyOnce()
    {
        int disposed = 0;
        var lifetime = new ContentResourceLifetime(() => Interlocked.Increment(ref disposed));
        Assert.True(lifetime.TryRetain());
        lifetime.Retire();
        Assert.False(lifetime.TryRetain());
        Assert.False(lifetime.TryDispose());
        lifetime.Release();
        Assert.True(lifetime.TryDispose());
        Assert.False(lifetime.TryDispose());
        Assert.Equal(1, disposed);
        await Task.CompletedTask;
    }
}
