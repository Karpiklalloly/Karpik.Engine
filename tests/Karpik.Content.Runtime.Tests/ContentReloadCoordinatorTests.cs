using System.Text;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class ContentReloadCoordinatorTests
{
    private static readonly AssetId A = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly AssetId B = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    private static ContentManifestEntry Entry(AssetId id, string locator) => new(id, "raw", locator, "settings", locator, locator, 1, []);
    private static ContentManifest Manifest(string locator) => new(1, [Entry(A, locator)]);
    private sealed class Store : IContentStore, IContentStoreLifetime
    {
        public int References;
        public void Retain() => Interlocked.Increment(ref References);
        public void Release() => Assert.True(Interlocked.Decrement(ref References) >= 0);
        public ReadOnlyMemory<byte> Get(string locator) => Encoding.UTF8.GetBytes(locator);
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Task.FromResult(Get(locator));
    }
    [Fact]
    public async Task PublicationWaitsForBoundary_ReportsDenseChangesAndKeepsReferencesExplicit()
    {
        var store = new Store();
        await using var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(Manifest("a"), store);
        await registry.LoadAsync(new AssetRef<string>(A));
        await using var coordinator = new ContentReloadCoordinator(registry);
        Task<ContentReloadResult> result = coordinator.EnqueueAsync(Manifest("b"), store);
        await WaitReadyAsync(coordinator);
        Assert.False(result.IsCompleted);
        Assert.True(registry.IsAlive(new AssetRef<string>(A)));
        await PublishAsync(coordinator, result);
        Assert.Equal(ContentReloadOutcome.Applied, (await result).Outcome);
        Assert.False(registry.IsAlive(new AssetRef<string>(A)));
        Assert.True(registry.TryResolve<string>(A, out var current));
        Assert.Equal(2u, current.Version);
        Assert.Equal(new ContentAssetChange(A, ContentAssetChangeKind.Updated), Assert.Single(coordinator.Changes.ToArray()));
        Assert.Equal(1ul, coordinator.Sequence);
        coordinator.PublishBoundary();
        Assert.Empty(coordinator.Changes.ToArray());
    }

    private sealed class GatedStore : IContentStore, IContentStoreLifetime
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<ReadOnlyMemory<byte>> Bytes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int References;
        public void Retain() => Interlocked.Increment(ref References);
        public void Release() => Assert.True(Interlocked.Decrement(ref References) >= 0);
        public ReadOnlyMemory<byte> Get(string locator) => throw new NotSupportedException();
        public async Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            return await Bytes.Task.WaitAsync(ct);
        }
    }

    [Fact]
    public async Task LatestPendingSupersedesIntermediate_UsesActualBasisAndFreezesCallerDataAndLease()
    {
        var original = new Store();
        await using var registry = new ContentRegistry(null!, original);
        registry.RegisterManifest(Manifest("a"), original);
        await registry.LoadAsync(new AssetRef<string>(A));
        await using var coordinator = new ContentReloadCoordinator(registry);
        var activeStore = new GatedStore();
        Task<ContentReloadResult> active = coordinator.EnqueueAsync(Manifest("b"), activeStore);
        await activeStore.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var supersededStore = new Store();
        Task<ContentReloadResult> middle = coordinator.EnqueueAsync(new(1, [Entry(B, "c")]), supersededStore);
        var dependencies = new List<AssetId> { A };
        var entries = new List<ContentManifestEntry> { Entry(A, "b"), new(B, "raw", "d", "settings", "d", "d", 1, dependencies) };
        var latestStore = new Store();
        latestStore.Retain(); // Caller-owned lease, independently released after enqueue.
        Task<ContentReloadResult> latest = coordinator.EnqueueAsync(new(1, entries), latestStore);
        latestStore.Release();
        entries.Clear();
        dependencies.Add(new AssetId(Guid.NewGuid()));
        Assert.Equal(ContentReloadOutcome.Superseded, (await middle).Outcome);
        Assert.Equal(0, supersededStore.References);
        Assert.Equal(1, latestStore.References);
        Assert.False(active.IsCompleted);
        activeStore.Bytes.SetResult(Encoding.UTF8.GetBytes("b"));
        await PublishAsync(coordinator, active);
        Assert.Equal(ContentReloadOutcome.Applied, (await active).Outcome);
        await PublishAsync(coordinator, latest);
        Assert.Equal(ContentReloadOutcome.Applied, (await latest).Outcome);
        Assert.Equal(new ContentAssetChange(B, ContentAssetChangeKind.Added), Assert.Single(coordinator.Changes.ToArray()));
        Assert.True(registry.TryResolve<string>(A, out var a));
        Assert.Equal(2u, a.Version);
        Assert.True(registry.TryResolve<string>(B, out _));
        Assert.Equal(2ul, coordinator.Sequence);
    }

    [Fact]
    public async Task InvalidPreparationFailsAndRetries_AndNoOpLeavesSequenceAndReferenceAlive()
    {
        var store = new Store();
        await using var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(Manifest("a"), store);
        await registry.LoadAsync(new AssetRef<string>(A));
        await using var coordinator = new ContentReloadCoordinator(registry);
        var invalid = new ContentManifest(1, [Entry(A, "a"), Entry(A, "duplicate")]);
        Assert.Equal(ContentReloadOutcome.Failed, (await coordinator.EnqueueAsync(invalid, store).WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.Equal(ContentReloadOutcome.Failed, (await coordinator.EnqueueAsync(new(99, []), store)).Outcome);
        Assert.True(registry.IsAlive(new AssetRef<string>(A)));
        Task<ContentReloadResult> badSize = coordinator.EnqueueAsync(Manifest("long"), store);
        Assert.Equal(ContentReloadOutcome.Failed, (await badSize.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        Assert.Null(coordinator.Failure);
        Task<ContentReloadResult> unchanged = coordinator.EnqueueAsync(Manifest("a"), store);
        await PublishAsync(coordinator, unchanged);
        Assert.Equal(ContentReloadOutcome.Applied, (await unchanged).Outcome);
        Assert.Empty(coordinator.Changes.ToArray());
        Assert.Equal(0ul, coordinator.Sequence);
        Assert.True(registry.IsAlive(new AssetRef<string>(A)));
        Task<ContentReloadResult> retry = coordinator.EnqueueAsync(Manifest("b"), store);
        await PublishAsync(coordinator, retry);
        Assert.Equal(ContentReloadOutcome.Applied, (await retry).Outcome);
        Assert.Equal(1ul, coordinator.Sequence);
    }

    [Fact]
    public async Task FullManifestReplacementDelivers8192BoundedChanges_ThenReusesBuffer()
    {
        static AssetId Id(int index) => new(new Guid(index, 0, 0, new byte[8]));
        var store = new Store();
        var before = new ContentManifest(1, Enumerable.Range(1, ContentRegistry.MaximumAssets).Select(i => Entry(Id(i), "a")).ToArray());
        var after = new ContentManifest(1, Enumerable.Range(5000, ContentRegistry.MaximumAssets).Select(i => Entry(Id(i), "b")).ToArray());
        await using var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(before, store);
        await using var coordinator = new ContentReloadCoordinator(registry);
        var oversized = new ContentManifest(1, Enumerable.Range(1, ContentRegistry.MaximumAssets + 1).Select(i => Entry(Id(i), "a")).ToArray());
        Assert.Equal(ContentReloadOutcome.Failed, (await coordinator.EnqueueAsync(oversized, store)).Outcome);
        Task<ContentReloadResult> replacement = coordinator.EnqueueAsync(after, store);
        await PublishAsync(coordinator, replacement);
        Assert.Equal(ContentReloadOutcome.Applied, (await replacement).Outcome);
        var changes = coordinator.Changes.ToArray();
        Assert.Equal(ContentReloadCoordinator.MaximumChanges, changes.Length);
        Assert.Equal(ContentRegistry.MaximumAssets, changes.Count(c => c.Kind == ContentAssetChangeKind.Added));
        Assert.Equal(ContentRegistry.MaximumAssets, changes.Count(c => c.Kind == ContentAssetChangeKind.Removed));
        Assert.Equal(changes.OrderBy(c => c.AssetId).ToArray(), changes);
        Task<ContentReloadResult> remove = coordinator.EnqueueAsync(new(1, []), store);
        await PublishAsync(coordinator, remove);
        Assert.Equal(ContentReloadOutcome.Applied, (await remove).Outcome);
        Assert.Equal(ContentRegistry.MaximumAssets, coordinator.Changes.Length);
        Task<ContentReloadResult> restore = coordinator.EnqueueAsync(before, store);
        await PublishAsync(coordinator, restore);
        Assert.Equal(ContentReloadOutcome.Applied, (await restore).Outcome);
        Assert.Equal(ContentRegistry.MaximumAssets, coordinator.Changes.Length);
    }

    [Fact]
    public async Task StopSettlesPreparingAndPendingRequests_AndRejectsFurtherEnqueues()
    {
        var store = new Store();
        await using var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(Manifest("a"), store);
        await registry.LoadAsync(new AssetRef<string>(A));
        await using var coordinator = new ContentReloadCoordinator(registry);
        var gated = new GatedStore();
        Task<ContentReloadResult> active = coordinator.EnqueueAsync(Manifest("b"), gated);
        await gated.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<ContentReloadResult> pending = coordinator.EnqueueAsync(Manifest("c"), store);
        coordinator.RequestStop();
        Assert.Equal(ContentReloadOutcome.Failed, (await active.WaitAsync(TimeSpan.FromSeconds(1))).Outcome);
        Assert.Equal(ContentReloadOutcome.Failed, (await pending.WaitAsync(TimeSpan.FromSeconds(1))).Outcome);
        Assert.Equal(ContentReloadOutcome.Failed, (await coordinator.EnqueueAsync(Manifest("d"), store)).Outcome);
        Assert.False(coordinator.PublishBoundary());
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(registry.IsAlive(new AssetRef<string>(A)));
        Assert.Equal(0, gated.References);
    }

    [Fact]
    public async Task StopAndReadyPublicationRaceHasOneWinner_AndNoTaskIsStranded()
    {
        for (int iteration = 0; iteration < 20; iteration++)
        {
            var store = new Store();
            await using var registry = new ContentRegistry(null!, store);
            registry.RegisterManifest(Manifest("a"), store);
            await using var coordinator = new ContentReloadCoordinator(registry);
            Task<ContentReloadResult> delivery = coordinator.EnqueueAsync(Manifest("b"), store);
            await WaitReadyAsync(coordinator);
            Task<bool> boundary = Task.Run(coordinator.PublishBoundary);
            Task stop = Task.Run(coordinator.RequestStop);
            await Task.WhenAll(boundary, stop);
            Assert.Equal(await boundary ? ContentReloadOutcome.Applied : ContentReloadOutcome.Failed,
                (await delivery.WaitAsync(TimeSpan.FromSeconds(2))).Outcome);
        }
    }

    [Fact]
    public async Task WarmBoundaryAndUpdateDeliveryAllocateNothingOnCallerThread()
    {
        var store = new Store();
        await using var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(Manifest("a"), store);
        await using var coordinator = new ContentReloadCoordinator(registry);
        for (int i = 0; i < 100; i++) coordinator.PublishBoundary();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
        {
            coordinator.PublishBoundary();
            _ = coordinator.Changes.Length;
            _ = coordinator.Sequence;
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        for (int iteration = 0; iteration < 12; iteration++)
        {
            Task<ContentReloadResult> result = coordinator.EnqueueAsync(Manifest(iteration % 2 == 0 ? "b" : "a"), store);
            await WaitReadyAsync(coordinator);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool committed = coordinator.PublishBoundary();
            int count = coordinator.Changes.Length;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(committed);
            Assert.Equal(1, count);
            if (iteration != 0) Assert.Equal(0, allocated); // First commit activation is warmup.
            Assert.Equal(ContentReloadOutcome.Applied, (await result.WaitAsync(TimeSpan.FromSeconds(2))).Outcome);
        }
    }

    private sealed class Payload(ContentResourceLifetime lifetime) : IContentTrackedResource
    {
        public ContentResourceLifetime Lifetime => lifetime;
    }
    private sealed class Loader(Func<Payload> create) : IContentPayloadLoader
    {
        public bool CanLoad(Type type, string declared) => type == typeof(Payload);
        public ValueTask<object> LoadAsync(Type type, string declared, ReadOnlyMemory<byte> bytes, CancellationToken ct) => ValueTask.FromResult<object>(create());
    }

    [Fact]
    public async Task AppliedAndStopResultsDoNotWaitForRendererClaims_ButDisposalJoinsRetirement()
    {
        var old = new ContentResourceLifetime(() => { });
        Assert.True(old.TryRetain());
        int loads = 0;
        var loader = new Loader(() => new Payload(Interlocked.Increment(ref loads) == 1 ? old : new ContentResourceLifetime(() => { })));
        var store = new Store();
        await using var registry = new ContentRegistry(null!, store, [loader]);
        registry.RegisterManifest(Manifest("a"), store);
        await registry.LoadAsync(new AssetRef<Payload>(A));
        await using var coordinator = new ContentReloadCoordinator(registry);
        Task disposal;
        try
        {
            Task<ContentReloadResult> apply = coordinator.EnqueueAsync(Manifest("b"), store);
            await PublishAsync(coordinator, apply);
            Assert.Equal(ContentReloadOutcome.Applied, (await apply.WaitAsync(TimeSpan.FromSeconds(1))).Outcome);
            Assert.False(old.Completion.IsCompleted);
            Task<ContentReloadResult> pending = coordinator.EnqueueAsync(Manifest("c"), store);
            coordinator.RequestStop();
            Assert.Equal(ContentReloadOutcome.Failed, (await pending.WaitAsync(TimeSpan.FromSeconds(1))).Outcome);
            disposal = coordinator.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
        }
        finally { old.Release(); }
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(old.Completion.IsCompletedSuccessfully);
        Assert.Null(coordinator.Failure);
    }

    [Fact]
    public async Task BackendRetirementFailureRemainsApplied_AndStickyDiagnosticRejectsLaterUpdates()
    {
        int loads = 0;
        var loader = new Loader(() => new Payload(new ContentResourceLifetime(Interlocked.Increment(ref loads) == 1
            ? () => throw new InvalidOperationException("backend retirement failure") : () => { })));
        var store = new Store();
        await using var registry = new ContentRegistry(null!, store, [loader]);
        registry.RegisterManifest(Manifest("a"), store);
        await registry.LoadAsync(new AssetRef<Payload>(A));
        await using var coordinator = new ContentReloadCoordinator(registry);
        Task<ContentReloadResult> apply = coordinator.EnqueueAsync(Manifest("b"), store);
        await PublishAsync(coordinator, apply);
        Assert.Equal(ContentReloadOutcome.Applied, (await apply.WaitAsync(TimeSpan.FromSeconds(1))).Outcome);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (coordinator.Failure == null) await Task.Delay(1, timeout.Token);
        Assert.Contains("backend retirement failure", coordinator.Failure.ToString());
        Assert.Equal(ContentReloadOutcome.Failed, (await coordinator.EnqueueAsync(Manifest("c"), store)).Outcome);
        Assert.True(registry.TryResolve<Payload>(A, out var current));
        Assert.Equal(2u, current.Version);
        Assert.True(registry.IsAlive(current));
    }

    private static async Task WaitReadyAsync(ContentReloadCoordinator coordinator)
    {
        var field = typeof(ContentReloadCoordinator).GetField("_ready", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            object? ready = field.GetValue(coordinator);
            if (ready != null && (int)ready.GetType().GetField("State", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ready)! == 0) return;
            await Task.Delay(1, timeout.Token);
        }
    }

    private static async Task PublishAsync(ContentReloadCoordinator coordinator, Task<ContentReloadResult> result)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!result.IsCompleted)
        {
            if (coordinator.PublishBoundary()) return;
            await Task.Delay(1, timeout.Token);
        }
    }
}
