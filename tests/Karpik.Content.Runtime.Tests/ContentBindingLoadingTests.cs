using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Serialization;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class ContentBindingLoadingTests
{
    private static readonly AssetId First = new(new Guid("577c125d-c4cf-4435-b4be-9fa877e4ce2e"));
    private static readonly AssetId Second = new(new Guid("de4048be-7b48-4af0-a7f5-d59e0edb166f"));

    [Fact]
    public async Task Initial_load_missing_and_readdition_are_published_at_boundaries()
    {
        var store = new Store();
        store.Add("one", "loaded");
        var registry = Registry(store, Manifest(store, (First, "one", "raw")));
        await using var bindings = Bindings(registry);
        var loaded = bindings.Bind(First, "fallback");
        var missing = bindings.Bind(Second, "missing");
        await Pump(bindings, () => State(bindings, loaded).State == ContentBindingState.Ready);
        Assert.True(bindings.TryGet(loaded, out var value));
        Assert.Equal("loaded", value);
        Assert.Equal(ContentBindingState.Missing, State(bindings, missing).State);

        store.Add("two", "appeared");
        await Commit(registry, bindings, Manifest(store, (First, "one", "raw"), (Second, "two", "raw")));
        await Pump(bindings, () => State(bindings, missing).State == ContentBindingState.Ready);
        Assert.True(bindings.TryGet(missing, out value));
        Assert.Equal("appeared", value);
    }

    [Fact]
    public async Task Json_projection_updates_and_a_projection_error_keeps_the_last_good_ui()
    {
        var store = new Store();
        store.Add("old", "{\"Title\":\"old title\"}");
        var registry = Registry(store, Manifest(store, (First, "old", "raw-json")));
        await using var bindings = Bindings(registry);
        var ui = bindings.BindJson(First, BindingJsonContext.Default.BindingUi, new BindingUi("fallback"));
        await Pump(bindings, () => State(bindings, ui).State == ContentBindingState.Ready);
        Assert.True(bindings.TryGet(ui, out var value));
        Assert.Equal("old title", value.Title);

        store.Add("bad", "{\"Title\":42}");
        await Commit(registry, bindings, Manifest(store, (First, "bad", "raw-json")));
        await Pump(bindings, () => State(bindings, ui).State == ContentBindingState.Failed);
        Assert.True(bindings.TryGet(ui, out value));
        Assert.NotNull(value);
        Assert.Equal("old title", value.Title);
        Assert.NotNull(bindings.GetError(ui));
        Assert.Equal(1u, State(bindings, ui).PresentedVersion);
        Assert.Equal(2u, State(bindings, ui).RequestedVersion);

        store.Add("new", "{\"Title\":\"new title\"}");
        await Commit(registry, bindings, Manifest(store, (First, "new", "raw-json")));
        await Pump(bindings, () => State(bindings, ui).State == ContentBindingState.Ready);
        Assert.True(bindings.TryGet(ui, out value));
        Assert.NotNull(value);
        Assert.Equal("new title", value.Title);
        Assert.Null(bindings.GetError(ui));
        Assert.Equal(3u, State(bindings, ui).PresentedVersion);
    }

    [Fact]
    public async Task Prepared_native_replacement_is_adopted_synchronously_before_new_draws()
    {
        var store = new Store();
        store.Add("old", "green");
        var registry = Registry(store, Manifest(store, (First, "old", "texture")), new TrackedLoader());
        var bindings = Bindings(registry);
        var texture = bindings.Bind<TrackedPayload>(First);
        await Pump(bindings, () => State(bindings, texture).State == ContentBindingState.Ready);
        Assert.True(bindings.TryGet(texture, out var old));
        Assert.True(old.Lifetime.TryRetain()); // An unfinished old frame owns an independent claim.

        store.Add("new", "red");
        using var update = await registry.PrepareAsync(Manifest(store, (First, "new", "texture")));
        Assert.True(registry.TryCommit(update));
        bindings.AdvanceBoundary();
        Assert.True(bindings.TryGet(texture, out var current));
        Assert.Equal("red", current.Label);
        Assert.NotSame(old, current);
        Assert.Equal(2u, State(bindings, texture).PresentedVersion);
        Assert.False(old.Destroyed);
        old.Lifetime.Release();
        await update.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(old.Destroyed);

        await bindings.DisposeAsync();
        await registry.DisposeAsync();
        Assert.True(current.Destroyed);
    }

    [Fact]
    public async Task Slow_initial_load_does_not_delay_another_binding()
    {
        var store = new Store();
        store.Add("slow", "slow value"); store.Add("fast", "fast value");
        var gate = store.Gate("slow");
        var registry = Registry(store, Manifest(store, (First, "slow", "raw"), (Second, "fast", "raw")));
        await using var bindings = Bindings(registry);
        var slow = bindings.Bind(First, "waiting");
        var fast = bindings.Bind<string>(Second);
        await Pump(bindings, () => State(bindings, fast).State == ContentBindingState.Ready);
        Assert.False(gate.Release.Task.IsCompleted);
        Assert.True(bindings.TryGet(slow, out var value));
        Assert.Equal("waiting", value);
        gate.Release.TrySetResult();
        await Pump(bindings, () => State(bindings, slow).State == ContentBindingState.Ready);
    }

    [Fact]
    public async Task Removal_during_load_cannot_publish_the_obsolete_result_and_readd_recovers()
    {
        var store = new Store(); store.Add("old", "obsolete"); store.Add("new", "current");
        var gate = store.Gate("old");
        var registry = Registry(store, Manifest(store, (First, "old", "raw")));
        await using var bindings = Bindings(registry);
        var binding = bindings.Bind(First, "fallback");
        bindings.AdvanceBoundary();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var removal = await registry.PrepareAsync(new ContentManifest(1, []));
        Assert.True(registry.TryCommit(removal));
        bindings.AdvanceBoundary();
        Assert.Equal(ContentBindingState.Missing, State(bindings, binding).State);
        gate.Release.TrySetResult();
        await removal.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(bindings.TryGet(binding, out var value)); Assert.Equal("fallback", value);

        await Commit(registry, bindings, Manifest(store, (First, "new", "raw")));
        await Pump(bindings, () => State(bindings, binding).State == ContentBindingState.Ready);
        Assert.True(bindings.TryGet(binding, out value)); Assert.Equal("current", value);
        Assert.Equal(3u, State(bindings, binding).PresentedVersion);
    }

    [Fact]
    public async Task Explicit_retry_recovers_from_a_transient_load_failure()
    {
        var store = new Store(); store.Add("asset", "recovered"); store.FailNext = true;
        var registry = Registry(store, Manifest(store, (First, "asset", "raw")));
        await using var bindings = Bindings(registry);
        var binding = bindings.Bind(First, "fallback");
        await Pump(bindings, () => State(bindings, binding).State == ContentBindingState.Failed);
        Assert.NotNull(bindings.GetError(binding));
        Assert.True(bindings.Retry(binding));
        await Pump(bindings, () => State(bindings, binding).State == ContentBindingState.Ready);
        Assert.True(bindings.TryGet(binding, out var value)); Assert.Equal("recovered", value);
    }

    [Fact]
    public async Task Incompatible_existing_payload_type_is_failed_not_missing()
    {
        var store = new Store(); store.Add("asset", "string");
        var registry = Registry(store, Manifest(store, (First, "asset", "raw")));
        await registry.LoadAsync(new AssetRef<string>(First));
        await using var bindings = Bindings(registry);
        var binding = bindings.Bind<byte[]>(First);
        await Pump(bindings, () => State(bindings, binding).State == ContentBindingState.Failed);
        Assert.NotNull(bindings.GetError(binding));
    }

    [Fact]
    public async Task Release_bounds_outstanding_work_and_reuses_the_slot_after_drain()
    {
        var store = new Store(); store.Add("asset", "value");
        var gate = store.Gate("asset");
        var registry = Registry(store, Manifest(store, (First, "asset", "raw")));
        await using var bindings = new ContentBindings(registry, registry, new EmptyChanges(), new ContentBindingOptions { Capacity = 1 });
        var old = bindings.Bind<string>(First);
        bindings.AdvanceBoundary();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(bindings.Release(old));
        Assert.False(bindings.TryGet(old, out _));
        Assert.Throws<InvalidOperationException>(() => bindings.Bind<string>(First));
        gate.Release.TrySetResult();
        ContentBinding<string> current = default;
        await Pump(bindings, () =>
        {
            try { current = bindings.Bind<string>(First); return true; }
            catch (InvalidOperationException) { return false; }
        });
        Assert.NotEqual(old, current);
        await Pump(bindings, () => State(bindings, current).State == ContentBindingState.Ready);
    }

    [Fact]
    public async Task Ready_reads_and_boundary_adoption_allocate_nothing_on_the_caller_thread()
    {
        var store = new Store(); store.Add("old", "old"); store.Add("new", "new");
        var registry = Registry(store, Manifest(store, (First, "old", "raw")));
        await using var bindings = Bindings(registry);
        var binding = bindings.Bind<string>(First);
        await Pump(bindings, () => State(bindings, binding).State == ContentBindingState.Ready);
        for (int i = 0; i < 100; i++) bindings.AdvanceBoundary();
        using var update = await registry.PrepareAsync(Manifest(store, (First, "new", "raw")));
        Assert.True(registry.TryCommit(update));
        long start = GC.GetAllocatedBytesForCurrentThread();
        bindings.AdvanceBoundary();
        for (int i = 0; i < 1000; i++) { bindings.AdvanceBoundary(); bindings.TryGet(binding, out _); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, bytes);
        Assert.True(bindings.TryGet(binding, out var value)); Assert.Equal("new", value);
        await update.Completion;
    }

    [Fact]
    public async Task Stopping_one_owner_does_not_cancel_a_shared_load_for_another_owner()
    {
        var store = new Store(); store.Add("asset", "shared result");
        var gate = store.Gate("asset");
        var registry = Registry(store, Manifest(store, (First, "asset", "raw")));
        var observed = new ObservedRegistry(registry);
        var first = new ContentBindings(observed, observed, new EmptyChanges());
        await using var second = new ContentBindings(observed, observed, new EmptyChanges());
        first.Bind<string>(First); first.AdvanceBoundary();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var current = second.Bind<string>(First); second.AdvanceBoundary();
        await observed.SecondLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await first.DisposeAsync();
        gate.Release.TrySetResult();
        await Pump(second, () => State(second, current).State == ContentBindingState.Ready);
        Assert.True(second.TryGet(current, out var value));
        Assert.Equal("shared result", value);
    }

    [Fact]
    public async Task Repeated_projection_release_and_slot_reuse_drain_all_work()
    {
        var store = new Store(); store.Add("ui", "{\"Title\":\"value\"}");
        var registry = Registry(store, Manifest(store, (First, "ui", "raw-json")));
        await using var bindings = new ContentBindings(registry, registry, new EmptyChanges(), new ContentBindingOptions { Capacity = 1 });
        for (int i = 0; i < 128; i++)
        {
            ContentBinding<BindingUi> binding = default;
            await Pump(bindings, () =>
            {
                try { binding = bindings.BindJson(First, BindingJsonContext.Default.BindingUi); return true; }
                catch (InvalidOperationException) { return false; }
            });
            await Pump(bindings, () => State(bindings, binding).State == ContentBindingState.Ready);
            Assert.True(bindings.Release(binding));
            Assert.False(bindings.TryGet(binding, out _));
        }
    }

    private static ContentBindingStatus State<T>(ContentBindings bindings, ContentBinding<T> binding)
    {
        Assert.True(bindings.TryGetStatus(binding, out var status)); return status;
    }
    private static async Task Pump(ContentBindings bindings, Func<bool> complete)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            bindings.AdvanceBoundary();
            if (complete()) return;
            await Task.Delay(1, timeout.Token);
        }
    }
    private static async Task Commit(ContentRegistry registry, ContentBindings bindings, ContentManifest manifest)
    {
        using var update = await registry.PrepareAsync(manifest);
        Assert.True(registry.TryCommit(update)); bindings.AdvanceBoundary();
        await update.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static ContentBindings Bindings(ContentRegistry registry) => new(registry, registry, new EmptyChanges());
    private static ContentRegistry Registry(Store store, ContentManifest manifest, params IContentPayloadLoader[] loaders)
    {
        var registry = new ContentRegistry(null!, store, loaders); registry.RegisterManifest(manifest, store); return registry;
    }
    private static ContentManifest Manifest(Store store, params (AssetId Id, string Locator, string Kind)[] items) => new(1,
        items.Select(item => new ContentManifestEntry(item.Id, item.Kind, item.Locator, "settings", item.Locator,
            item.Locator, store.Bytes[item.Locator].Length, [])).ToArray());
    private sealed class EmptyChanges : IContentChangeSource
    {
        public ReadOnlySpan<ContentAssetChange> Changes => [];
        public ulong Sequence => 0;
    }
    private sealed class Store : IContentStore
    {
        public readonly ConcurrentDictionary<string, byte[]> Bytes = new();
        private readonly ConcurrentDictionary<string, LoadGate> _gates = new();
        public bool FailNext;
        public void Add(string key, string value) => Bytes[key] = Encoding.UTF8.GetBytes(value);
        public LoadGate Gate(string key) { var gate = new LoadGate(); _gates[key] = gate; return gate; }
        public ReadOnlyMemory<byte> Get(string locator) => Bytes[locator];
        public async Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default)
        {
            if (FailNext) { FailNext = false; throw new IOException("Transient source failure."); }
            if (_gates.TryGetValue(locator, out var gate))
            {
                gate.Entered.TrySetResult(); await gate.Release.Task.WaitAsync(ct);
            }
            return Get(locator);
        }
    }
    private sealed class LoadGate
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class ObservedRegistry(ContentRegistry inner) : IContentRegistry, IContentUpdateRegistry
    {
        private int _loads;
        public readonly TaskCompletionSource SecondLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void RegisterManifest(ContentManifest manifest, IContentStore store) => inner.RegisterManifest(manifest, store);
        public bool IsAlive<T>(AssetRef<T> asset) => inner.IsAlive(asset);
        public bool IsAlive(AssetId id, uint version) => inner.IsAlive(id, version);
        public bool TryGet<T>(AssetRef<T> asset, out AssetLease<T> lease) => inner.TryGet(asset, out lease);
        public Task LoadAsync<T>(AssetRef<T> asset, CancellationToken ct = default)
        {
            Task task = inner.LoadAsync(asset, ct);
            if (Interlocked.Increment(ref _loads) >= 2) SecondLoad.TrySetResult();
            return task;
        }
        public bool TryResolve<T>(AssetId id, out AssetRef<T> reference) => inner.TryResolve(id, out reference);
        public Task<PreparedContentUpdate> PrepareAsync(ContentManifest manifest, CancellationToken cancellationToken = default) => inner.PrepareAsync(manifest, cancellationToken);
        public Task<PreparedContentUpdate> PrepareAsync(ContentManifest manifest, IContentStore store, CancellationToken cancellationToken = default) => inner.PrepareAsync(manifest, store, cancellationToken);
        public bool TryCommit(PreparedContentUpdate update) => inner.TryCommit(update);
    }
    private sealed class TrackedPayload(string label) : IContentTrackedResource
    {
        public readonly string Label = label;
        public bool Destroyed;
        public ContentResourceLifetime Lifetime { get; } = new(() => { });
        public TrackedPayload(string label, bool tracked) : this(label)
        {
            Lifetime = new ContentResourceLifetime(() => Destroyed = true);
        }
    }
    private sealed class TrackedLoader : IContentPayloadLoader
    {
        public bool CanLoad(Type type, string kind) => type == typeof(TrackedPayload) && kind == "texture";
        public ValueTask<object> LoadAsync(Type type, string kind, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
            new(new TrackedPayload(Encoding.UTF8.GetString(bytes.Span), true));
    }
}

public sealed record BindingUi(string Title);

[JsonSerializable(typeof(BindingUi))]
internal partial class BindingJsonContext : JsonSerializerContext;
