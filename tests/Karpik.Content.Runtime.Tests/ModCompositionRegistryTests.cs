using System.Text;
using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class ModCompositionRegistryTests
{
    private static readonly AssetId A = new(new Guid(1, 0, 0, new byte[8]));
    private static readonly AssetId B = new(new Guid(2, 0, 0, new byte[8]));
    private static readonly AssetId C = new(new Guid(3, 0, 0, new byte[8]));
    private static readonly AssetId D = new(new Guid(4, 0, 0, new byte[8]));

    [Fact]
    public async Task DeferredStartupKeepsLegacyAvailableWithoutPublishingIdsBeforeVersionOne()
    {
        var store = new Store("g");
        await using var registry = new ContentRegistry(null!, store, deferredStartup: new DeferredContentStartup());
        var manifest = Manifest(Entry(A, "base"));
        registry.RegisterManifest(manifest, store);

        Assert.Equal("g", ReadLegacy(registry, "base"));
        Assert.False(registry.TryResolve<string>(A, out _));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.PrepareAsync(manifest));
        registry.CompleteStartup([], []);
        Assert.Equal(1u, (await Load(registry, A)).Reference.Version);
        Assert.Equal("g", ReadLegacy(registry, "base"));
        Assert.Throws<InvalidOperationException>(() => registry.CompleteStartup([], []));
    }

    [Fact]
    public async Task OrderedPackagesUseOriginalPayloadsEvenForTargetsDeclaredLater()
    {
        var game = new Store("g");
        var first = Package("z", new Store("z"), [Entry(B, "base")], [new(null, A, B), new("a", C, B)]);
        var last = Package("a", new Store("a"), [Entry(C, "other")], [new(null, A, C), new("z", B, C)]);
        await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup([first, last], ["a", "z"]);

        Assert.Equal((1u, "a"), await VersionAndPayload(registry, A));
        Assert.Equal((1u, "a"), await VersionAndPayload(registry, B));
        Assert.Equal((1u, "z"), await VersionAndPayload(registry, C));
        Assert.Equal("g", ReadLegacy(registry, "base"));
        Assert.False(registry.TryResolveArtifact("other", out _, out _));
    }

    [Fact]
    public async Task InvalidRulesDistinguishMissingOwnersAndPreserveTheLastValidReplacement()
    {
        var game = new Store("g");
        var first = Package("patch", new Store("p"), [Entry(B, "patch")], [new(null, A, B)]);
        var last = Package("later", new Store("l"), [Entry(C, "later", "other")],
            [new("optional", D, C), new("installed", D, C), new(null, D, C), new(null, A, C), new(null, A, B), new("patch", A, C)]);
        var diagnostics = new List<ContentDiagnostic>();
        await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup([first, last], ["patch", "later", "installed"], diagnostics.Add);

        Assert.Collection(diagnostics,
            d => CheckDiagnostic(d, ContentDiagnosticSeverity.Warning, D, last),
            d => CheckDiagnostic(d, ContentDiagnosticSeverity.Error, D, last),
            d => CheckDiagnostic(d, ContentDiagnosticSeverity.Error, D, last),
            d => CheckDiagnostic(d, ContentDiagnosticSeverity.Error, A, last),
            d => CheckDiagnostic(d, ContentDiagnosticSeverity.Error, A, last),
            d => CheckDiagnostic(d, ContentDiagnosticSeverity.Error, A, last));
        Assert.Equal("p", (await Load(registry, A)).Value);
    }

    [Fact]
    public async Task PackageConflictsAndInvalidLocalGraphsAreRejectedAsUnits()
    {
        var game = new Store("g");
        var first = Package("first", new Store("f"), [Entry(B, "first")], []);
        var baseCollision = Package("collision", new Store("x"), [Entry(A, "collision"), Entry(C, "leak")], []);
        var otherCollision = Package("second", new Store("y"), [Entry(B, "collision"), Entry(C, "leak")], []);
        var crossDependency = Package("cross", new Store("d"), [Entry(C, "cross", "raw", B)], []);
        var cycle = Package("cycle", new Store("c"), [Entry(C, "one", "raw", D), Entry(D, "two", "raw", C)], []);
        var duplicateOne = Package("duplicate", new Store("u"), [Entry(C, "dup-one")], []);
        var duplicateTwo = Package("duplicate", new Store("v"), [Entry(D, "dup-two")], []);
        var invalidSchema = new ModContentPackage("schema", new ContentManifest(2, [Entry(D, "schema")]), new Store("s"), [], "schema.json");
        var targetingRejected = Package("targeting", new Store("t"), [], [new("cross", C, B)]);
        ModContentPackage[] packages = [first, baseCollision, otherCollision, crossDependency, cycle, duplicateOne,
            duplicateTwo, invalidSchema, targetingRejected];
        var diagnostics = new List<ContentDiagnostic>();
        await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup(packages, packages.Select(p => p.ModId).ToArray(), diagnostics.Add);

        Assert.Equal(packages.Skip(1).Take(7).Select(p => p.SourcePath), diagnostics.Take(7).Select(d => d.RelativePath));
        Assert.Equal(8, diagnostics.Count);
        CheckDiagnostic(diagnostics[^1], ContentDiagnosticSeverity.Error, C, targetingRejected);
        Assert.All(diagnostics, d => Assert.Equal(ContentDiagnosticSeverity.Error, d.Severity));
        Assert.Contains(C.ToCanonicalString(), diagnostics[^1].Message);
        Assert.Equal("g", (await Load(registry, A)).Value);
        Assert.Equal("f", (await Load(registry, B)).Value);
        Assert.False(registry.TryResolve<string>(C, out _));
        Assert.False(registry.TryResolve<string>(D, out _));
        Assert.All(packages.Skip(1).Take(7), p => Assert.Equal(0, ((Store)p.Store).Claims));
    }

    [Fact]
    public async Task EffectiveGraphCycleSkipsOnlyTheRuleThatIntroducesIt()
    {
        var game = new Store("g");
        // A <- B is valid. C <- D would add C -> B, forming a cycle with B -> C.
        var patch = Package("patch", new Store("p", locator => Encoding.UTF8.GetBytes(locator)),
            [Entry(B, "b", "raw", C), Entry(C, "c"), Entry(D, "d", "raw", B)],
            [new(null, A, B), new("patch", C, D)]);
        var diagnostics = new List<ContentDiagnostic>();
        await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup([patch], ["patch"], diagnostics.Add);

        CheckDiagnostic(Assert.Single(diagnostics), ContentDiagnosticSeverity.Error, C, patch);
        Assert.Contains("cycle", diagnostics[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("b", (await Load(registry, A)).Value);
        Assert.Equal("c", (await Load(registry, C)).Value);
    }

    [Fact]
    public async Task HiddenBaseChangeSwitchesLegacyAndDefaultStoreWithoutConsumingAnEffectiveVersion()
    {
        var game = new Store("g");
        var candidate = new Store("n");
        var patch = Package("patch", new Store("p"), [Entry(B, "patch")], [new(null, A, B)]);
        await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup([patch], ["patch"]);
        var original = (await Load(registry, A)).Reference;

        using (var update = await registry.PrepareAsync(Manifest(Entry(A, "renamed")), candidate))
        {
            Assert.Equal("g", ReadLegacy(registry, "base"));
            await Commit(registry, update);
        }
        Assert.False(registry.TryResolveArtifact("base", out _, out _));
        Assert.Equal("n", ReadLegacy(registry, "renamed"));
        Assert.Equal("p", Payload(registry, original));
        Assert.Equal((1u, "p"), await VersionAndPayload(registry, B));
        Assert.Equal(0, game.Claims);

        using var removal = await registry.PrepareModUpdateAsync([], []);
        await Commit(registry, removal);
        Assert.False(registry.TryGet(original, out _));
        Assert.False(registry.TryResolve<string>(B, out _));
        Assert.Equal((2u, "n"), await VersionAndPayload(registry, A));
    }

    [Fact]
    public async Task BaseReloadChangesVisibleAssetsAndRechecksDisappearingTargetsAgainstCommittedPackages()
    {
        var game = new Store("g");
        var candidate = new Store("n");
        var patch = Package("patch", new Store("p"), [Entry(B, "patch")], [new(null, A, B)]);
        await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base"), Entry(C, "visible-old")), game);
        registry.CompleteStartup([patch], ["patch"]);
        var visible = (await Load(registry, C)).Reference;

        var diagnostics = new List<ContentDiagnostic>();
        using (var update = await registry.PrepareAsync(Manifest(Entry(C, "visible-new")), candidate, diagnostics.Add, default))
            await Commit(registry, update);
        CheckDiagnostic(Assert.Single(diagnostics), ContentDiagnosticSeverity.Error, A, patch);
        Assert.False(registry.TryResolve<string>(A, out _));
        Assert.False(registry.TryGet(visible, out _));
        Assert.Equal((2u, "n"), await VersionAndPayload(registry, C));
        Assert.Equal((1u, "p"), await VersionAndPayload(registry, B));

        using var returnTarget = await registry.PrepareAsync(Manifest(Entry(A, "base-new"), Entry(C, "visible-new")));
        await Commit(registry, returnTarget);
        Assert.Equal((3u, "p"), await VersionAndPayload(registry, A));
        Assert.Equal((1u, "p"), await VersionAndPayload(registry, B));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdenticalModEntriesWithDifferentSourceOrStoreAreNewGenerations(bool changeOwner)
    {
        var game = new Store("g");
        var old = new Store("o");
        var next = changeOwner ? old : new Store("n");
        var patch = Package("patch", old, [Entry(B, "same")], [new(null, A, B)]);
        var replacement = Package(changeOwner ? "new-owner" : "patch", next, [Entry(B, "same")], [new(null, A, B)]);
        await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup([patch], [patch.ModId]);
        var oldRef = (await Load(registry, A)).Reference;

        using var update = await registry.PrepareModUpdateAsync([replacement], [replacement.ModId]);
        await Commit(registry, update);
        Assert.False(registry.TryGet(oldRef, out _));
        Assert.Equal((2u, changeOwner ? "o" : "n"), await VersionAndPayload(registry, A));
        Assert.Equal((2u, changeOwner ? "o" : "n"), await VersionAndPayload(registry, B));
        if (!changeOwner) Assert.Equal(0, old.Claims);
    }

    [Fact]
    public async Task FullyShadowedPackagesAreRetainedThroughPrepareAbortCommitAndShutdown()
    {
        var game = new Store("g");
        var old = new Store("o");
        var next = new Store("n");
        var first = Package("first", old, [Entry(B, "old")], [new(null, A, B)]);
        var replaced = Package("first", next, [Entry(B, "new")], [new(null, A, B)]);
        var winner = Package("winner", new Store("w"), [Entry(C, "winner")], [new(null, A, C), new("first", B, C)]);
        await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup([first, winner], ["first", "winner"]);
        var original = (await Load(registry, A)).Reference;
        Assert.Equal(1, old.Claims); // No effective slots use this package.

        using (var abort = await registry.PrepareModUpdateAsync([replaced, winner], ["first", "winner"]))
        {
            try { Assert.Equal(1, next.Claims); }
            finally { abort.Dispose(); await abort.Completion; }
        }
        Assert.Equal(0, next.Claims);
        Assert.Equal(1, old.Claims);
        using (var update = await registry.PrepareModUpdateAsync([replaced, winner], ["first", "winner"]))
            await Commit(registry, update);
        Assert.Equal(0, old.Claims);
        Assert.Equal(1, next.Claims);
        Assert.Equal("w", Payload(registry, original));
        Assert.Equal((1u, "w"), await VersionAndPayload(registry, B));

        using (var removeWinner = await registry.PrepareModUpdateAsync([replaced], ["first"]))
            await Commit(registry, removeWinner);
        Assert.Equal((2u, "n"), await VersionAndPayload(registry, A));
        Assert.Equal((2u, "n"), await VersionAndPayload(registry, B));
        await registry.DisposeAsync();
        Assert.Equal(0, next.Claims);
        Assert.Equal(0, ((Store)winner.Store).Claims);
    }

    [Fact]
    public async Task RefreshConflictsRejectTheWholeCandidateAndLeaveTheCurrentPackagesUsable()
    {
        var game = new Store("g");
        var patch = Package("patch", new Store("p"), [Entry(B, "patch")], [new(null, A, B)]);
        var incoming = Package("incoming", new Store("i"), [Entry(C, "incoming")], []);
        var invalid = Package("invalid", new Store("x"), [Entry(A, "duplicate")], []);
        var diagnostics = new List<ContentDiagnostic>();
        await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup([patch], ["patch"]);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            using var rejected = await registry.PrepareModUpdateAsync([incoming, invalid], ["incoming", "invalid"], diagnostics.Add);
            rejected.Dispose();
            await rejected.Completion;
        });
        Assert.Equal(invalid.SourcePath, Assert.Single(diagnostics).RelativePath);
        Assert.Equal((1u, "p"), await VersionAndPayload(registry, A));
        Assert.Equal((1u, "p"), await VersionAndPayload(registry, B));
        Assert.False(registry.TryResolve<string>(C, out _));
        Assert.Equal(0, ((Store)incoming.Store).Claims);
        Assert.Equal(0, ((Store)invalid.Store).Claims);
    }

    [Fact]
    public async Task RetiredModRetainsStoresUntilTheLateReadAndRendererClaimBothSettle()
    {
        var lateRead = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var game = new Store("g");
        var old = new Store("o", readAsync: locator => locator == "late" ? lateRead.Task : Task.FromResult<ReadOnlyMemory<byte>>("o"u8.ToArray()));
        var patch = Package("patch", old, [Entry(B, "patch"), Entry(C, "late")], [new(null, A, B)]);
        int destroyed = 0;
        var tracked = new Tracked(new ContentResourceLifetime(() => Interlocked.Increment(ref destroyed)));
        await using var registry = new ContentRegistry(null!, game, [new Loader(tracked)], new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup([patch], ["patch"]);
        await registry.LoadAsync(new AssetRef<Tracked>(A));
        var loading = registry.LoadAsync(new AssetRef<string>(C));
        using var removal = await registry.PrepareModUpdateAsync([], []);
        Assert.True(tracked.Lifetime.TryRetain());
        try
        {
            Assert.True(registry.TryCommit(removal));
            Assert.False(registry.TryResolve<string>(C, out _));
            Assert.True(Volatile.Read(ref old.Claims) > 0);
            Assert.False(removal.Completion.IsCompleted);
            lateRead.SetResult("o"u8.ToArray());
            Exception? lateError = await Record.ExceptionAsync(() => loading);
            if (lateError != null) Assert.IsType<InvalidOperationException>(lateError);
            Assert.True(Volatile.Read(ref old.Claims) > 0);
            Assert.False(removal.Completion.IsCompleted);
        }
        finally
        {
            lateRead.TrySetResult("o"u8.ToArray());
            tracked.Lifetime.Release();
            removal.Dispose();
            await removal.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(1, destroyed);
        Assert.Equal(0, old.Claims);
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("abort")]
    [InlineData("shutdown")]
    public async Task FailedSlotDrainReleasesUnrelatedRootClaimsOnCommitAbortAndShutdown(string operation)
    {
        var game = new Store("g");
        var unsafeStore = new Store("o");
        var safeStore = new Store("s");
        var emptyStore = new Store("e");
        ModContentPackage[] packages =
        [
            Package("unsafe", unsafeStore, [Entry(B, "override")], [new(null, A, B)]),
            Package("safe", safeStore, [Entry(C, "safe")], []),
            Package("empty", emptyStore, [], [])
        ];
        string[] installed = ["unsafe", "safe", "empty"];
        var drainError = new InvalidOperationException("Backend destruction failed.");
        var tracked = new Tracked(new ContentResourceLifetime(() => throw drainError));
        await using var registry = new ContentRegistry(null!, game, [new Loader(tracked)], new DeferredContentStartup());
        var manifest = Manifest(Entry(A, "base"));
        registry.RegisterManifest(manifest, game);
        registry.CompleteStartup(operation == "abort" ? [] : packages, operation == "abort" ? [] : installed);
        await registry.LoadAsync(new AssetRef<Tracked>(A));

        AggregateException error;
        if (operation == "shutdown")
            error = await Assert.ThrowsAsync<AggregateException>(() => registry.DisposeAsync().AsTask());
        else
        {
            using var update = operation == "abort"
                ? await registry.PrepareModUpdateAsync(packages, installed)
                : await registry.PrepareModUpdateAsync([], []);
            if (operation == "commit") Assert.True(registry.TryCommit(update));
            else update.Dispose();
            error = await Assert.ThrowsAsync<AggregateException>(() => update.Completion);
            var sticky = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.PrepareAsync(manifest));
            Assert.Same(error, sticky.InnerException);
        }

        Assert.Same(drainError, Assert.Single(error.Flatten().InnerExceptions));
        Assert.Equal(0, safeStore.Claims);
        Assert.Equal(0, emptyStore.Claims);
        Assert.Equal(1, unsafeStore.Claims); // Only the unsafe renderer slot stays pinned.
        Assert.Equal(operation == "shutdown" ? 0 : 2, game.Claims);
    }

    [Fact]
    public async Task FailedLazyLoadRetryDoesNotErasePriorUnsafeCleanupAtShutdown()
    {
        using var cancellation = new CancellationTokenSource();
        var drainError = new InvalidOperationException("Backend destruction failed.");
        var retryError = new IOException("Retry read failed before decode.");
        var tracked = new Tracked(new ContentResourceLifetime(() => throw drainError));
        int reads = 0;
        var unsafeStore = new Store("o", readAsync: _ => ++reads == 1
            ? Task.FromResult<ReadOnlyMemory<byte>>("o"u8.ToArray())
            : Task.FromException<ReadOnlyMemory<byte>>(retryError));
        var game = new Store("g");
        var independent = new Store("i");
        await using var registry = new ContentRegistry(null!, game, [new Loader(tracked, cancellation.Cancel)], new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup([Package("unsafe", unsafeStore, [Entry(B, "mod")], []), Package("independent", independent, [], [])],
            ["unsafe", "independent"]);
        var reference = new AssetRef<Tracked>(B);

        var load = await Assert.ThrowsAsync<AggregateException>(() => registry.LoadAsync(reference, cancellation.Token));
        Assert.Collection(load.InnerExceptions, e => Assert.IsType<OperationCanceledException>(e), e => Assert.Same(drainError, e));
        Assert.Same(retryError, await Assert.ThrowsAsync<IOException>(() => registry.LoadAsync(reference)));
        Assert.Equal(2, reads);
        Assert.Equal(2, unsafeStore.Claims);

        var shutdown = await Record.ExceptionAsync(() => registry.DisposeAsync().AsTask());

        Assert.Equal(1, unsafeStore.Claims);
        Assert.Equal(0, game.Claims);
        Assert.Equal(0, independent.Claims);
        Assert.Same(drainError, Assert.Single(Assert.IsType<AggregateException>(shutdown).InnerExceptions));
    }

    [Fact]
    public async Task FinalStoreReleaseErrorsDoNotSkipLaterSlotsOrRootClaims()
    {
        var slotError = new IOException("Historical base store deletion failed.");
        var rootError = new IOException("Root-only package deletion failed.");
        var historical = new Store("h", finalRelease: () => throw slotError);
        var current = new Store("c");
        var faultyRoot = new Store("f", finalRelease: () => throw rootError);
        var healthyRoot = new Store("s");
        var original = Entry(A, "unchanged");
        await using var registry = new ContentRegistry(null!, historical, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(original, Entry(B, "old")), historical);
        registry.CompleteStartup([Package("faulty", faultyRoot, [], []), Package("healthy", healthyRoot, [], [])], ["faulty", "healthy"]);
        using (var next = await registry.PrepareAsync(Manifest(original, Entry(B, "new")), current))
            await Commit(registry, next);
        Assert.Equal(1, historical.Claims); // Only the unchanged base slot retains this store.
        Assert.Equal(2, current.Claims); // A later slot plus the default root claim.

        var error = await Record.ExceptionAsync(() => registry.DisposeAsync().AsTask());

        Assert.Equal(0, current.Claims);
        Assert.Equal(0, healthyRoot.Claims);
        Assert.Equal(0, historical.Claims);
        Assert.Equal(0, faultyRoot.Claims);
        var failures = Assert.IsType<AggregateException>(error).Flatten().InnerExceptions;
        Assert.Collection(failures, e => Assert.Same(slotError, e), e => Assert.Same(rootError, e));
    }

    [Fact]
    public async Task WarmedOverrideReadsAndRootPublicationAllocateZeroBytes()
    {
        var game = new Store("g");
        var source = new Store("p");
        var patch = Package("patch", source, [Entry(B, "patch")], [new(null, A, B)]);
        await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
        registry.RegisterManifest(Manifest(Entry(A, "base")), game);
        registry.CompleteStartup([patch], ["patch"]);
        await Load(registry, A);
        using (var warm = await registry.PrepareModUpdateAsync([patch], ["patch"])) await Commit(registry, warm);
        Assert.Equal(1000, ReadEffective());

        using var update = await registry.PrepareModUpdateAsync([patch], ["patch", "new-owner-without-side-package"]);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool committed = registry.TryCommit(update);
        int reads = ReadEffective();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await update.Completion;
        Assert.True(committed);
        Assert.Equal(1000, reads);
        Assert.Equal(0, allocated);

        int ReadEffective()
        {
            int ready = 0;
            for (int i = 0; i < 1000; ++i)
                if (registry.TryResolve<string>(A, out var reference) && registry.TryGet(reference, out var lease) &&
                    lease.Payload == "p" && registry.IsAlive(reference)) ready++;
            return ready;
        }
    }

    [Fact]
    public async Task SeededPackageCombinationsFollowTheSuppliedOrderAndFallBackToTheOriginal()
    {
        var random = new Random(305);
        string[] ids = ["z", "a", "m"];
        AssetId[] replacements = [B, C, D];
        for (int trial = 0; trial < 128; trial++)
        {
            var game = new Store("g");
            var packages = new List<ModContentPackage>();
            string expected = "g";
            foreach (int i in Enumerable.Range(0, 3).OrderBy(_ => random.Next()))
            {
                if (random.Next(3) == 0) continue;
                bool valid = random.Next(2) == 0;
                string id = ids[i];
                packages.Add(Package(id, new Store(id), [Entry(replacements[i], id)], [new(null, A, valid ? replacements[i] : A)]));
                if (valid) expected = id;
            }
            await using var registry = new ContentRegistry(null!, game, deferredStartup: new DeferredContentStartup());
            registry.RegisterManifest(Manifest(Entry(A, "base")), game);
            registry.CompleteStartup(packages, ids);
            Assert.Equal((1u, expected), await VersionAndPayload(registry, A));
        }
    }

    private sealed record Tracked(ContentResourceLifetime Lifetime) : IContentTrackedResource;

    private sealed class Loader(Tracked old, Action? onDecoded = null) : IContentPayloadLoader
    {
        public bool CanLoad(Type type, string declaredType) => type == typeof(Tracked);
        public ValueTask<object> LoadAsync(Type type, string declaredType, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            var payload = bytes.Span[0] == (byte)'o' ? old : new Tracked(new ContentResourceLifetime(() => { }));
            onDecoded?.Invoke();
            return ValueTask.FromResult<object>(payload);
        }
    }

    private static async Task Commit(ContentRegistry registry, PreparedContentUpdate update)
    {
        Assert.True(registry.TryCommit(update));
        await update.Completion;
    }

    private static void CheckDiagnostic(ContentDiagnostic diagnostic, ContentDiagnosticSeverity severity, AssetId target,
        ModContentPackage source)
    {
        Assert.Equal(severity, diagnostic.Severity);
        Assert.Equal(Path.Combine(source.SourcePath, "overrides.json"), diagnostic.RelativePath);
        Assert.Contains(target.ToCanonicalString(), diagnostic.Message);
    }

    private static ModContentPackage Package(string id, IContentStore store, ContentManifestEntry[] entries,
        ModContentOverride[] rules) => new(id, Manifest(entries), store, rules, id);

    private static async Task<(uint, string)> VersionAndPayload(ContentRegistry registry, AssetId id)
    {
        var loaded = await Load(registry, id);
        return (loaded.Reference.Version, loaded.Value);
    }

    private static ContentManifest Manifest(params ContentManifestEntry[] entries) => new(1, entries);

    private static ContentManifestEntry Entry(AssetId id, string name, string declaredType = "raw", params AssetId[] dependencies) =>
        new(id, declaredType, name, "settings", name, name, 1, dependencies);

    private static string ReadLegacy(ContentRegistry registry, string logicalName)
    {
        using var stream = registry.TryOpenArtifact(logicalName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<(AssetRef<string> Reference, string Value)> Load(ContentRegistry registry, AssetId id)
    {
        Assert.True(registry.TryResolve<string>(id, out var reference));
        await registry.LoadAsync(reference);
        return (reference, Payload(registry, reference));
    }

    private static string Payload(ContentRegistry registry, AssetRef<string> reference)
    {
        Assert.True(registry.TryGet(reference, out var lease));
        return lease.Payload;
    }

    private sealed class Store(string value, Func<string, byte[]>? read = null,
        Func<string, Task<ReadOnlyMemory<byte>>>? readAsync = null, Action? finalRelease = null) : IContentStore, IContentStoreLifetime
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(value);
        public int Claims;
        public void Retain() => Interlocked.Increment(ref Claims);
        public void Release()
        {
            int remaining = Interlocked.Decrement(ref Claims);
            Assert.True(remaining >= 0);
            if (remaining == 0) finalRelease?.Invoke();
        }
        public ReadOnlyMemory<byte> Get(string locator) => read?.Invoke(locator) ?? _bytes;
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) =>
            readAsync?.Invoke(locator) ?? Task.FromResult(Get(locator));
    }
}
