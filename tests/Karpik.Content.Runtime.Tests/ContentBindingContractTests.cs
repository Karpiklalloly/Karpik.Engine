using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class ContentBindingContractTests
{
    private static readonly AssetId Id = new(new Guid("85165171-9659-4f43-8065-c1f3f2351b2a"));

    [Fact]
    public void Handles_and_status_are_unmanaged_and_default_handles_are_invalid()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ContentBinding<object>>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ContentBindingStatus>());
        using var owner = new Owner();
        Assert.False(owner.Bindings.TryGet(default(ContentBinding<string>), out _));
        Assert.False(owner.Bindings.TryGetStatus(default(ContentBinding<string>), out _));
        Assert.False(owner.Bindings.Release(default(ContentBinding<string>)));
        Assert.False(owner.Bindings.Retry(default(ContentBinding<string>)));
    }

    [Fact]
    public void Registration_has_pending_state_and_preserves_explicit_value_type_fallback()
    {
        using var owner = new Owner();
        var empty = owner.Bindings.Bind<int>(Id);
        var fallback = owner.Bindings.Bind(Id, 0);
        Assert.False(owner.Bindings.TryGet(empty, out _));
        Assert.True(owner.Bindings.TryGet(fallback, out int value));
        Assert.Equal(0, value);
        Assert.True(owner.Bindings.TryGetStatus(fallback, out var status));
        Assert.Equal(new ContentBindingStatus(ContentBindingState.Pending, true, true, 0, 0), status);
    }

    [Fact]
    public void Null_fallback_is_empty_and_json_metadata_is_required()
    {
        using var owner = new Owner();
        var binding = owner.Bindings.Bind<string?>(Id, null);
        Assert.False(owner.Bindings.TryGet(binding, out _));
        Assert.Throws<ArgumentNullException>(() => owner.Bindings.BindJson<string>(Id, null!));
    }

    [Fact]
    public void Foreign_and_released_handles_cannot_read_release_or_retry_other_slots()
    {
        using var first = new Owner(1);
        using var second = new Owner(1);
        var old = first.Bindings.Bind(Id, "old");
        Assert.False(second.Bindings.TryGet(old, out _));
        Assert.False(second.Bindings.TryGetStatus(old, out _));
        Assert.False(second.Bindings.Release(old));
        Assert.False(second.Bindings.Retry(old));
        Assert.True(first.Bindings.Release(old));
        Assert.False(first.Bindings.Release(old));
        var current = first.Bindings.Bind(Id, "new");
        Assert.NotEqual(old, current);
        Assert.False(first.Bindings.TryGet(old, out _));
        Assert.False(first.Bindings.TryGetStatus(old, out _));
        Assert.False(first.Bindings.Retry(old));
        Assert.True(first.Bindings.TryGet(current, out var value));
        Assert.Equal("new", value);
    }

    [Fact]
    public void Exhaustion_is_explicit_and_does_not_damage_existing_bindings()
    {
        using var owner = new Owner(1);
        var current = owner.Bindings.Bind(Id, "working");
        Assert.Throws<InvalidOperationException>(() => owner.Bindings.Bind<string>(Id));
        Assert.True(owner.Bindings.TryGet(current, out var value));
        Assert.Equal("working", value);
        Assert.True(owner.Bindings.Release(current));
        _ = owner.Bindings.Bind<string>(Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4097)]
    [InlineData(int.MaxValue)]
    public void Invalid_capacity_fails_before_creating_an_owner(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Owner(capacity));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public void Exact_capacity_boundaries_are_usable(int capacity)
    {
        using var owner = new Owner(capacity);
        for (int i = 0; i < capacity; i++) _ = owner.Bindings.Bind<int>(Id);
        Assert.Throws<InvalidOperationException>(() => owner.Bindings.Bind<int>(Id));
    }

    [Fact]
    public void Tracked_generation_resources_cannot_be_constant_fallbacks()
    {
        using var owner = new Owner();
        Assert.Throws<ArgumentException>(() => owner.Bindings.Bind(Id, new TrackedFallback()));
    }

    [Fact]
    public void Warm_handle_reads_and_status_observations_allocate_nothing()
    {
        using var owner = new Owner();
        var binding = owner.Bindings.Bind(Id, 42);
        for (int i = 0; i < 100; i++) { owner.Bindings.TryGet(binding, out _); owner.Bindings.TryGetStatus(binding, out _); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int i = 0; i < 10_000; i++)
        {
            owner.Bindings.TryGet(binding, out int value);
            owner.Bindings.TryGetStatus(binding, out var status);
            sum += value + (status.HasValue ? 1 : 0);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(430_000, sum);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Disposal_invalidates_handles_and_prevents_further_registration()
    {
        var owner = new Owner();
        var binding = owner.Bindings.Bind(Id, "current");
        owner.Dispose();
        Assert.False(owner.Bindings.TryGet(binding, out _));
        Assert.False(owner.Bindings.TryGetStatus(binding, out _));
        Assert.False(owner.Bindings.Release(binding));
        Assert.False(owner.Bindings.Retry(binding));
        Assert.Throws<ObjectDisposedException>(() => owner.Bindings.Bind<string>(Id));
    }

    private sealed class TrackedFallback : IContentTrackedResource
    {
        public ContentResourceLifetime Lifetime { get; } = new(() => { });
    }

    private sealed class Owner : IDisposable
    {
        public ContentBindings Bindings { get; }
        public Owner(int capacity = ContentBindingOptions.DefaultCapacity)
        {
            var registry = new EmptyRegistry();
            Bindings = new ContentBindings(registry, registry, new EmptyChanges(), new ContentBindingOptions { Capacity = capacity });
        }
        public void Dispose() => Bindings.DisposeAsync().GetAwaiter().GetResult();
    }

    private sealed class EmptyChanges : IContentChangeSource
    {
        public ReadOnlySpan<ContentAssetChange> Changes => [];
        public ulong Sequence => 0;
    }

    private sealed class EmptyRegistry : IContentRegistry, IContentUpdateRegistry
    {
        public void RegisterManifest(ContentManifest manifest, IContentStore store) => throw new NotSupportedException();
        public bool IsAlive<T>(AssetRef<T> asset) => false;
        public bool IsAlive(AssetId id, uint version) => false;
        public bool TryGet<T>(AssetRef<T> asset, out AssetLease<T> lease) { lease = default; return false; }
        public Task LoadAsync<T>(AssetRef<T> asset, CancellationToken ct = default) => throw new NotSupportedException();
        public bool TryResolve<T>(AssetId id, out AssetRef<T> reference) { reference = default; return false; }
        public Task<PreparedContentUpdate> PrepareAsync(ContentManifest manifest, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PreparedContentUpdate> PrepareAsync(ContentManifest manifest, IContentStore store, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool TryCommit(PreparedContentUpdate update) => throw new NotSupportedException();
    }
}
