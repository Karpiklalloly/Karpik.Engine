using System.Text;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;
using ModBatch = (System.Collections.Generic.IReadOnlyList<Karpik.Content.Runtime.ModContentPackage> Packages,
    System.Collections.Generic.IReadOnlyCollection<string> InstalledModIds,
    System.Collections.Generic.IReadOnlyList<Karpik.Content.Runtime.ModContentCapture> Captures);

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
        public Exception? ReleaseError;
        public void Release()
        {
            Assert.True(Interlocked.Decrement(ref References) >= 0);
            if (Interlocked.Exchange(ref ReleaseError, null) is { } error) throw error;
        }
        public ReadOnlyMemory<byte> Get(string locator)
        {
            Assert.True(Volatile.Read(ref References) > 0);
            return Encoding.UTF8.GetBytes(locator);
        }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BaseReloadReportsMissingTargetOrRejectsOriginalIdCollision(bool collision)
    {
        var original = new Store();
        var mod = new Store();
        await using var registry = new ContentRegistry(null!, original, deferredStartup: new());
        registry.RegisterManifest(Manifest("a"), original);
        registry.CompleteStartup([Package(mod, "m")], ["mod"]);
        await using var coordinator = new ContentReloadCoordinator(registry);
        var incoming = new Store();
        var diagnostics = new List<ContentDiagnostic>();
        Task<ContentReloadResult> result = coordinator.EnqueueAsync(new(1, collision ? [Entry(B, "b")] : []), incoming, diagnostics.Add);
        await PublishAsync(coordinator, result);
        Assert.Equal(collision ? ContentReloadOutcome.Failed : ContentReloadOutcome.Applied, (await result.WaitAsync(TestTimeout)).Outcome);
        Assert.Equal(collision ? "KCR302" : "KCR303", Assert.Single(diagnostics).Code);
        Assert.True(mod.References > 0);
        Assert.True(registry.TryResolve<string>(B, out var own));
        Assert.Equal(1u, own.Version);
        if (collision)
        {
            Assert.Empty(coordinator.Changes.ToArray());
            Assert.Equal("m", await ReadAsync(registry, A));
            await coordinator.DisposeAsync();
            Assert.Equal(0, incoming.References);
        }
        else
        {
            Assert.False(registry.TryResolve<string>(A, out _));
            Assert.Equal(new ContentAssetChange(A, ContentAssetChangeKind.Removed), Assert.Single(coordinator.Changes.ToArray()));
            Task<ContentReloadResult> restore = coordinator.EnqueueAsync(Manifest("b"), incoming);
            await PublishAsync(coordinator, restore);
            Assert.Equal(ContentReloadOutcome.Applied, (await restore.WaitAsync(TestTimeout)).Outcome);
            Assert.Equal("m", await ReadAsync(registry, A));
        }
    }

    [Theory]
    [InlineData("base-first")]
    [InlineData("mod-first")]
    [InlineData("superseded-base-moves-after-mod")]
    public async Task BaseAndModQueueUseActualEnqueueOrderWithoutLosingEitherLayer(string order)
    {
        var original = new Store();
        var mod = new Store();
        var nextBase = new Store();
        await using var registry = new ContentRegistry(null!, original);
        registry.RegisterManifest(Manifest("a"), original);
        Assert.Equal("a", await ReadAsync(registry, A));
        await using var coordinator = new ContentReloadCoordinator(registry);
        var gated = new GatedStore();
        Task<ContentReloadResult> active = coordinator.EnqueueAsync(Manifest("g"), gated);
        await gated.Entered.Task.WaitAsync(TestTimeout);

        Task<ContentReloadResult>? queuedBase = null;
        var supersededStore = new Store();
        if (order == "base-first") queuedBase = coordinator.EnqueueAsync(Manifest("c"), nextBase);
        if (order == "superseded-base-moves-after-mod") queuedBase = coordinator.EnqueueAsync(Manifest("d"), supersededStore);
        var package = Package(mod, "m");
        Task<ContentReloadResult> refresh = coordinator.EnqueueModRefreshAsync((previous, ct) =>
        {
            Assert.Empty(previous);
            Assert.True(registry.TryResolveArtifact(order == "base-first" ? "c" : "g", out _, out _));
            return Batch(Claim(package));
        });
        Assert.False(refresh.IsCompleted);
        if (order != "base-first")
        {
            var superseded = queuedBase;
            queuedBase = coordinator.EnqueueAsync(Manifest("c"), nextBase);
            if (superseded != null)
            {
                Assert.Equal(ContentReloadOutcome.Superseded, (await superseded.WaitAsync(TestTimeout)).Outcome);
                Assert.Equal(0, supersededStore.References);
            }
        }
        Task<ContentReloadResult> second = coordinator.EnqueueModRefreshAsync((_, _) => throw new InvalidOperationException("Busy callback ran"));
        Assert.Equal(ContentReloadOutcome.Busy, (await second.WaitAsync(TestTimeout)).Outcome);
        gated.Bytes.SetResult(Encoding.UTF8.GetBytes("g"));
        await PublishAsync(coordinator, active);
        Assert.Equal(ContentReloadOutcome.Applied, (await active.WaitAsync(TestTimeout)).Outcome);

        if (order == "base-first")
        {
            await PublishAsync(coordinator, queuedBase!);
            Assert.Equal(ContentReloadOutcome.Applied, (await queuedBase!.WaitAsync(TestTimeout)).Outcome);
            Assert.False(refresh.IsCompleted);
            Assert.Equal("c", await ReadAsync(registry, A));
            Assert.False(registry.TryResolve<string>(B, out _));
        }
        await PublishAsync(coordinator, refresh);
        Assert.Equal(ContentReloadOutcome.Applied, (await refresh.WaitAsync(TestTimeout)).Outcome);
        Assert.Equal("m", await ReadAsync(registry, A));
        Assert.Equal("m", await ReadAsync(registry, B));
        if (order != "base-first")
        {
            Assert.True(registry.TryResolve<string>(A, out var previousReference));
            ulong oldSequence = coordinator.Sequence;
            Assert.False(queuedBase!.IsCompleted);
            Assert.False(registry.TryResolveArtifact("c", out _, out _));
            await PublishAsync(coordinator, queuedBase);
            Assert.Equal(ContentReloadOutcome.Applied, (await queuedBase.WaitAsync(TestTimeout)).Outcome);
            Assert.Empty(coordinator.Changes.ToArray());
            Assert.Equal(oldSequence, coordinator.Sequence);
            Assert.True(registry.TryResolve<string>(A, out var currentReference));
            Assert.Equal(previousReference, currentReference);
        }
        Assert.True(registry.TryResolveArtifact("c", out var locator, out var store));
        Assert.Equal("c", locator);
        Assert.Same(nextBase, store);
        Assert.Equal("m", await ReadAsync(registry, A));
        Assert.Equal("m", await ReadAsync(registry, B));
    }

    [Fact]
    public async Task ModCommitsAllocateZeroWhenWarmed_NotifyOnlyEffectiveIds_AndReuseNoOpPackage()
    {
        var original = new Store();
        await using var registry = new ContentRegistry(null!, original);
        registry.RegisterManifest(Manifest("a"), original);
        var mod = new Store();
        var package = Package(mod, "m");
        await using var coordinator = new ContentReloadCoordinator(registry);
        for (int iteration = 0; iteration < 8; iteration++)
        {
            bool adding = iteration % 2 == 0;
            Task<ContentReloadResult> result = coordinator.EnqueueModRefreshAsync((previous, _) =>
            {
                if (adding) Assert.Empty(previous);
                else
                {
                    Assert.Same(package, Assert.Single(previous));
                    Assert.True(mod.References > 0);
                }
                return adding ? Batch(Claim(package)) : Batch();
            });
            await WaitReadyAsync(coordinator, result);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool committed = coordinator.PublishBoundary();
            int count = coordinator.Changes.Length;
            ulong sequence = coordinator.Sequence;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(committed);
            Assert.Equal(2, count);
            if (iteration != 0) Assert.Equal(0, allocated);
            Assert.Equal((ulong)iteration + 1, sequence);
            Assert.Equal(new[] { new ContentAssetChange(A, ContentAssetChangeKind.Updated),
                new ContentAssetChange(B, adding ? ContentAssetChangeKind.Added : ContentAssetChangeKind.Removed) }, coordinator.Changes.ToArray());
            Assert.Equal(ContentReloadOutcome.Applied, (await result.WaitAsync(TestTimeout)).Outcome);
            if (!adding) continue;

            Assert.True(registry.TryResolve<string>(A, out var reference));
            Task<ContentReloadResult> noOp = coordinator.EnqueueModRefreshAsync((previous, _) => Batch(Claim(Assert.Single(previous))));
            await WaitReadyAsync(coordinator, noOp);
            Assert.Equal(3, mod.References); // The root and two slots, no capture claim.
            Assert.True(coordinator.PublishBoundary());
            Assert.Equal(ContentReloadOutcome.Applied, (await noOp.WaitAsync(TestTimeout)).Outcome);
            Assert.Equal(sequence, coordinator.Sequence);
            Assert.Empty(coordinator.Changes.ToArray());
            Assert.True(registry.TryResolve<string>(A, out var current));
            Assert.Equal(reference, current);
        }
        await coordinator.DisposeAsync();
        Assert.Equal(0, mod.References);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedModPreparationOrWrapperReleaseLeavesSnapshotIntactAndDisposesEveryCapture(bool cleanupFailure)
    {
        var original = new Store();
        var lastGood = new Store();
        await using var registry = new ContentRegistry(null!, original, deferredStartup: new());
        registry.RegisterManifest(Manifest("a"), original);
        var good = Package(lastGood, "m");
        registry.CompleteStartup([good], ["mod"]);
        Assert.Equal("m", await ReadAsync(registry, A));
        int activeClaims = lastGood.References;
        var broken = new Store { ReleaseError = cleanupFailure ? new InvalidOperationException("wrapper release failed") : null };
        var unused = new Store();
        await using var coordinator = new ContentReloadCoordinator(registry);
        Task<ContentReloadResult> result = coordinator.EnqueueModRefreshAsync((previous, _) =>
        {
            Assert.Same(good, Assert.Single(previous));
            return Batch(Claim(Package(broken, cleanupFailure ? "n" : "long")),
                Claim(new ModContentPackage("unused", new(1, []), unused, [], "fixture")));
        });
        ContentReloadResult failed = await result.WaitAsync(TestTimeout);
        Assert.Equal(ContentReloadOutcome.Failed, failed.Outcome);
        Assert.Contains(cleanupFailure ? "wrapper release failed" : "size does not match", failed.Error);
        Assert.False(coordinator.PublishBoundary());
        Assert.Equal(0ul, coordinator.Sequence);
        Assert.Equal(activeClaims, lastGood.References);
        Assert.Equal("m", await ReadAsync(registry, B)); // Was still unread during the failed preparation.
        if (!cleanupFailure)
        {
            Assert.Null(coordinator.Failure);
            var retry = coordinator.EnqueueModRefreshAsync((previous, _) => Task.FromResult<ModBatch>((previous, ["mod"], [])));
            await PublishAsync(coordinator, retry);
            Assert.Equal(ContentReloadOutcome.Applied, (await retry.WaitAsync(TestTimeout)).Outcome);
        }
        await coordinator.DisposeAsync();
        Assert.Equal(0, broken.References);
        Assert.Equal(0, unused.References);
        Assert.Equal(activeClaims, lastGood.References);
    }

    [Fact]
    public async Task StopSettlesCaptureAndPendingBaseButDisposeJoinsCancellationAndLateCaptureCleanup()
    {
        var original = new Store();
        var mod = new Store();
        await using var registry = new ContentRegistry(null!, original);
        registry.RegisterManifest(Manifest("a"), original);
        await using var coordinator = new ContentReloadCoordinator(registry);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ContentReloadResult> refresh = coordinator.EnqueueModRefreshAsync(async (_, ct) =>
        {
            using var registration = ct.Register(() => cancelled.TrySetResult());
            entered.SetResult();
            await finishCapture.Task;
            return await Batch(Claim(Package(mod, "m")));
        });
        try
        {
            Assert.False(refresh.IsCompleted);
            await entered.Task.WaitAsync(TestTimeout);
            var duplicate = coordinator.EnqueueModRefreshAsync((_, _) => throw new InvalidOperationException("Busy callback ran"));
            Assert.Equal(ContentReloadOutcome.Busy, (await duplicate.WaitAsync(TestTimeout)).Outcome);
            var pending = coordinator.EnqueueAsync(Manifest("b"), original);
            coordinator.RequestStop();
            await cancelled.Task.WaitAsync(TestTimeout);
            Assert.Equal(ContentReloadOutcome.Failed, (await refresh.WaitAsync(TestTimeout)).Outcome);
            Assert.Equal(ContentReloadOutcome.Failed, (await pending.WaitAsync(TestTimeout)).Outcome);
            var rejected = coordinator.EnqueueModRefreshAsync((_, _) => throw new InvalidOperationException("Stopped callback ran"));
            Assert.Contains("stopping", (await rejected.WaitAsync(TestTimeout)).Error);
            Assert.False(coordinator.PublishBoundary());
            Assert.False(coordinator.DisposeAsync().IsCompleted);
        }
        finally { finishCapture.TrySetResult(); }
        await coordinator.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        Assert.Equal(0, mod.References);
        Assert.Equal("a", await ReadAsync(registry, A));
        Assert.False(registry.TryResolve<string>(B, out _));
    }

    [Fact]
    public async Task StopAcrossModPublicationBoundaryReportsTheActualWinnerAndReleasesAbortedCapture()
    {
        for (int iteration = 0; iteration < 12; iteration++)
        {
            var store = new Store();
            var mod = new Store();
            await using var registry = new ContentRegistry(null!, store);
            registry.RegisterManifest(Manifest("a"), store);
            await using var coordinator = new ContentReloadCoordinator(registry);
            var refresh = coordinator.EnqueueModRefreshAsync((_, _) => Batch(Claim(Package(mod, "m"))));
            await WaitReadyAsync(coordinator, refresh);
            bool committed;
            if (iteration == 0)
            {
                coordinator.RequestStop();
                committed = coordinator.PublishBoundary();
                Assert.False(committed);
            }
            else if (iteration == 1)
            {
                committed = coordinator.PublishBoundary();
                coordinator.RequestStop();
                Assert.True(committed);
            }
            else
            {
                Task<bool> publish = Task.Run(coordinator.PublishBoundary);
                await Task.WhenAll(publish, Task.Run(coordinator.RequestStop));
                committed = await publish;
            }
            Assert.Equal(committed ? ContentReloadOutcome.Applied : ContentReloadOutcome.Failed, (await refresh.WaitAsync(TestTimeout)).Outcome);
            await coordinator.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            Assert.Equal(committed, registry.TryResolve<string>(B, out _));
            Assert.Equal(committed ? 3 : 0, mod.References);
        }
    }

    [Fact]
    public async Task AppliedRefreshAllowsOnePendingButDoesNotCaptureUntilRetirementFinishes()
    {
        var old = new ContentResourceLifetime(() => { });
        var store = new Store();
        var mod = new Store();
        int loads = 0;
        var loader = new Loader(() => new Payload(Interlocked.Increment(ref loads) == 1 ? old : new ContentResourceLifetime(() => { })));
        await using var registry = new ContentRegistry(null!, store, [loader]);
        registry.RegisterManifest(Manifest("a"), store);
        await registry.LoadAsync(new AssetRef<Payload>(A));
        Assert.True(old.TryRetain());
        await using var coordinator = new ContentReloadCoordinator(registry);
        Task<ContentReloadResult> second;
        try
        {
            var package = Package(mod, "m");
            var first = coordinator.EnqueueModRefreshAsync((_, _) => Batch(Claim(package)));
            await WaitReadyAsync(coordinator, first);
            Assert.True(coordinator.PublishBoundary());
            var nextCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // Admission is open as soon as the boundary switches roots, not when retirement finishes.
            second = coordinator.EnqueueModRefreshAsync((previous, _) =>
            {
                Assert.True(old.Completion.IsCompletedSuccessfully);
                Assert.Same(package, Assert.Single(previous));
                nextCapture.SetResult();
                return Batch();
            });
            Assert.Equal(ContentReloadOutcome.Applied, (await first.WaitAsync(TestTimeout)).Outcome);
            var third = coordinator.EnqueueModRefreshAsync((_, _) => throw new InvalidOperationException("Busy callback ran"));
            Assert.Equal(ContentReloadOutcome.Busy, (await third.WaitAsync(TestTimeout)).Outcome);
            Assert.False(second.IsCompleted);
            Assert.False(nextCapture.Task.IsCompleted);
            Assert.False(old.Completion.IsCompleted);
        }
        finally { old.Release(); }
        await PublishAsync(coordinator, second);
        Assert.Equal(ContentReloadOutcome.Applied, (await second.WaitAsync(TestTimeout)).Outcome);
        await coordinator.DisposeAsync();
        Assert.Equal(0, mod.References);
    }

    [Fact]
    public async Task ExternalReservationRejectsOverlappingReloads_AndAbortPreservesRoot()
    {
        var store = new Store();
        var mod = new Store();
        await using var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(Manifest("a"), store);
        Assert.Equal("a", await ReadAsync(registry, A));
        await using var coordinator = new ContentReloadCoordinator(registry);
        var staged = await StageExternalAsync(coordinator, Package(mod, "m"));
        try
        {
            Assert.False(coordinator.PublishBoundary());
            Assert.False(registry.TryResolve<string>(B, out _));
            Assert.Equal("a", await ReadAsync(registry, A));
            var refresh = await coordinator.EnqueueModRefreshAsync((_, _) => throw new InvalidOperationException("Busy capture ran"))
                .WaitAsync(TestTimeout);
            Assert.Equal(ContentReloadOutcome.Busy, refresh.Outcome);
            var reload = await coordinator.EnqueueAsync(Manifest("b"), store).WaitAsync(TestTimeout);
            Assert.Equal(ContentReloadOutcome.Busy, reload.Outcome);
            await Assert.ThrowsAsync<InvalidOperationException>(() => registry.PrepareAsync(Manifest("b")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => registry.DisposeAsync().AsTask());
        }
        finally
        {
            staged.Dispose();
            await staged.Completion.WaitAsync(TestTimeout);
        }
        Assert.False(staged.TryCommitAtBarrier());
        Assert.Equal(0, mod.References);
        Assert.Equal("a", await ReadAsync(registry, A));
        Assert.False(registry.TryResolve<string>(B, out _));
        Assert.Empty(coordinator.Changes.ToArray());
        Assert.Equal(0ul, coordinator.Sequence);
        var retry = coordinator.EnqueueAsync(Manifest("b"), store);
        await PublishAsync(coordinator, retry);
        Assert.Equal(ContentReloadOutcome.Applied, (await retry).Outcome);
    }

    [Fact]
    public async Task ExternalCommitDeliversSortedChangesExactlyOnceAtTheFirstResumedBoundary()
    {
        var store = new Store();
        var mod = new Store();
        var package = Package(mod, "m");
        await using var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(Manifest("a"), store);
        await using var coordinator = new ContentReloadCoordinator(registry);
        for (int i = 0; i < 8; i++)
        {
            bool adding = i % 2 == 0;
            var staged = await StageExternalAsync(coordinator, adding ? package : null);
            try
            {
                Assert.False(coordinator.PublishBoundary()); // Never auto-publish the externally staged root.
                Assert.Equal(!adding, registry.TryResolve<string>(B, out _));

                long before = GC.GetAllocatedBytesForCurrentThread();
                bool committed = staged.TryCommitAtBarrier();
                bool automaticCommit = coordinator.PublishBoundary(); // No automatic work, but external changes must survive.
                int count = coordinator.Changes.Length;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(committed);
                Assert.False(automaticCommit);
                Assert.Equal(2, count);
                if (i != 0) Assert.Equal(0, allocated);
                Assert.Equal(adding, registry.TryResolve<string>(B, out _));
                Assert.Equal(new[] { new ContentAssetChange(A, ContentAssetChangeKind.Updated),
                    new ContentAssetChange(B, adding ? ContentAssetChangeKind.Added : ContentAssetChangeKind.Removed) }, coordinator.Changes.ToArray());
                Assert.False(staged.TryCommitAtBarrier());
                Assert.Equal((ulong)i + 1, coordinator.Sequence);
                Assert.False(coordinator.PublishBoundary());
                Assert.Empty(coordinator.Changes.ToArray());
                Assert.Equal((ulong)i + 1, coordinator.Sequence);
            }
            finally
            {
                staged.Dispose();
                await staged.Completion.WaitAsync(TestTimeout);
            }
        }
        var noOp = await StageExternalAsync(coordinator, null);
        try
        {
            Assert.True(noOp.TryCommitAtBarrier());
            Assert.False(coordinator.PublishBoundary());
            Assert.Empty(coordinator.Changes.ToArray());
            Assert.Equal(8ul, coordinator.Sequence);
        }
        finally
        {
            noOp.Dispose();
            await noOp.Completion.WaitAsync(TestTimeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownJoinsTheHostDecisionAndRetirement_WithoutRevokingReady(bool commit)
    {
        var old = new ContentResourceLifetime(() => { });
        int loads = 0;
        var loader = new Loader(() => new Payload(Interlocked.Increment(ref loads) == 1 ? old : new ContentResourceLifetime(() => { })));
        var store = new Store();
        var mod = new Store();
        await using var registry = new ContentRegistry(null!, store, [loader]);
        registry.RegisterManifest(Manifest("a"), store);
        await registry.LoadAsync(new AssetRef<Payload>(A));
        await using var coordinator = new ContentReloadCoordinator(registry);
        var staged = await StageExternalAsync(coordinator, Package(mod, "m"));
        Assert.True(old.TryRetain());
        try
        {
            coordinator.RequestStop();
            Task stopped = coordinator.DisposeAsync().AsTask();
            Assert.False(stopped.IsCompleted);
            Assert.False(coordinator.PublishBoundary());
            Assert.False(registry.TryResolve<string>(B, out _));
            if (commit)
            {
                Assert.True(await Task.Run(staged.TryCommitAtBarrier).WaitAsync(TestTimeout));
                Assert.True(registry.TryResolve<string>(B, out _));
                Assert.False(staged.Completion.IsCompleted);
                Assert.False(stopped.IsCompleted);
                Assert.False(old.Completion.IsCompleted);
            }
            else staged.Dispose();
        }
        finally
        {
            old.Release();
            staged.Dispose();
            await staged.Completion.WaitAsync(TestTimeout);
        }
        await coordinator.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        Assert.Equal(commit, old.Completion.IsCompletedSuccessfully);
        Assert.Equal(commit, registry.TryResolve<string>(B, out _));
        Assert.Equal(commit ? 3 : 0, mod.References);
    }

    [Fact]
    public async Task ExternalCaptureReservesAdmissionBeforeReady_AndStopWinsBeforeReady()
    {
        var store = new Store();
        var mod = new Store();
        await using var registry = new ContentRegistry(null!, store);
        registry.RegisterManifest(Manifest("a"), store);
        await using var coordinator = new ContentReloadCoordinator(registry);
        using var cleanup = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stagedTask = coordinator.PrepareModUpdateAsync(async (previous, ct) =>
        {
            Assert.Empty(previous);
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return await Batch(Claim(Package(mod, "m")));
        }, cancellationToken: cleanup.Token);
        try
        {
            await entered.Task.WaitAsync(TestTimeout);
            Assert.Null(await coordinator.PrepareModUpdateAsync((_, _) => throw new InvalidOperationException("Busy capture ran")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => registry.PrepareAsync(Manifest("b")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => registry.DisposeAsync().AsTask());
            Assert.Equal(ContentReloadOutcome.Busy, (await coordinator.EnqueueAsync(Manifest("b"), store)
                .WaitAsync(TestTimeout)).Outcome);
            Assert.Equal(ContentReloadOutcome.Busy, (await coordinator.EnqueueModRefreshAsync((_, _) => throw new InvalidOperationException("Busy capture ran"))
                .WaitAsync(TestTimeout)).Outcome);
            coordinator.RequestStop();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stagedTask.WaitAsync(TestTimeout));
            await coordinator.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            Assert.False(registry.TryResolve<string>(B, out _));
            Assert.False(coordinator.PublishBoundary());
        }
        finally
        {
            cleanup.Cancel();
            release.TrySetResult();
            try
            {
                if (await stagedTask.WaitAsync(TestTimeout) is { } staged)
                {
                    staged.Dispose();
                    await staged.Completion.WaitAsync(TestTimeout);
                }
            }
            catch (OperationCanceledException) { }
        }
        Assert.Equal(0, mod.References);
    }

    private static async Task<ContentReloadCoordinator.PreparedModUpdate> StageExternalAsync(
        ContentReloadCoordinator coordinator, ModContentPackage? package) => Assert.IsType<ContentReloadCoordinator.PreparedModUpdate>(
        await coordinator.PrepareModUpdateAsync((_, _) => package == null ? Batch() : Batch(Claim(package))).WaitAsync(TestTimeout));

    private static Task<ModBatch> Batch(params ModContentCapture[] captures) =>
        Task.FromResult<ModBatch>((captures.Select(c => c.Package).ToArray(), captures.Select(c => c.Package.ModId).ToArray(), captures));

    // Use the same wrapper/claim contract with fake stores and no filesystem scan.
    private static ModContentCapture Claim(ModContentPackage package)
    {
        ((IContentStoreLifetime)package.Store).Retain();
        return (ModContentCapture)Activator.CreateInstance(typeof(ModContentCapture),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [package], null)!;
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static ModContentPackage Package(IContentStore store, string value) =>
        new("mod", new(1, [Entry(B, value)]), store, [new(null, A, B)], "fixture");

    private static async Task<string> ReadAsync(ContentRegistry registry, AssetId id)
    {
        Assert.True(registry.TryResolve<string>(id, out var reference));
        await registry.LoadAsync(reference);
        Assert.True(registry.TryGet(reference, out var lease));
        return lease.Payload;
    }

    private static async Task WaitReadyAsync(ContentReloadCoordinator coordinator, Task<ContentReloadResult>? result = null)
    {
        var field = typeof(ContentReloadCoordinator).GetField("_ready", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            Assert.False(result?.IsCompleted == true, "The update completed without a ready publication.");
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
