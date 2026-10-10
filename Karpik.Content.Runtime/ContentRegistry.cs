using System.Composition;
using System.Runtime.CompilerServices;
using Autofac;
using Karpik.Content.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;

namespace Karpik.Content.Runtime;

[Export(typeof(IContentRegistry))]
[Export(typeof(ContentRegistry))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class ContentRegistry(IFileSystem fileSystem, IContentStore store) : IContentRegistry, IStartable
{
    private readonly Dictionary<AssetId, ContentSlot> _slots = new();
    private readonly object _initializationLock = new();
    private Dictionary<string, (string Locator, IContentStore Store)> _artifacts = new(StringComparer.Ordinal);
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IContentStore _startupStore = store;
    private IContentStore? _store;

    public void Start()
    {
        lock (_initializationLock)
        {
            if (_store is not null) return;
            string manifestPath = Path.Combine(_fileSystem.ContentPath, "manifest.json");
            using Stream stream = _fileSystem.OpenRead(manifestPath);
            RegisterManifest(ContentManifest.Load(stream), _startupStore);
        }
    }

    public void RegisterManifest(ContentManifest manifest, IContentStore store)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(store);
        lock (_initializationLock)
        {
            var artifacts = manifest.Entries.ToDictionary(e => e.LogicalName, e => (e.ArtifactLocator, store), StringComparer.Ordinal);
            _slots.Clear();
            foreach (var e in manifest.Entries.OrderBy(x => x.AssetId.Value))
            {
                _slots[e.AssetId] = new ContentSlot
                {
                    Id = e.AssetId,
                    State = SlotState.Unloaded,
                    Version = 0,
                    ArtifactLocator = e.ArtifactLocator,
                    DeclaredType = e.DeclaredType,
                    Dependencies = e.Dependencies.ToArray()
                };
            }
            Volatile.Write(ref _artifacts, artifacts);
            Volatile.Write(ref _store, store);
        }
    }

    public bool TryResolveArtifact(string logicalName, out string artifactLocator, out IContentStore store)
    {
        // A startable graphics backend can request an asset before Autofac starts this registry.
        if (Volatile.Read(ref _store) is null
            && _fileSystem.Exists(Path.Combine(_fileSystem.ContentPath, "manifest.json"))) Start();
        if (Volatile.Read(ref _artifacts).TryGetValue(logicalName, out var artifact))
        {
            artifactLocator = artifact.Locator;
            store = artifact.Store;
            return true;
        }
        artifactLocator = null!;
        store = null!;
        return false;
    }

    public bool IsAlive<T>(AssetRef<T> r) => IsAlive(r.Id, r.Version);

    public bool IsAlive(AssetId id, uint version)
    {
        if (!_slots.TryGetValue(id, out var slot))
            return false;
        // Volatile.Read fast path — lock-free read of State and Version
        var state = (SlotState)Volatile.Read(ref Unsafe.As<SlotState, int>(ref slot.State));
        if (state != SlotState.Loaded)
            return false;
        var ver = (uint)Volatile.Read(ref Unsafe.As<uint, int>(ref slot.Version));
        return ver == version;
    }

    public bool TryGet<T>(AssetRef<T> r, out AssetLease<T> lease)
    {
        if (_slots.TryGetValue(r.Id, out var slot))
        {
            // Volatile.Read fast path — zero-alloc, per-slot lock not taken on read
            var state = (SlotState)Volatile.Read(ref Unsafe.As<SlotState, int>(ref slot.State));
            if (state == SlotState.Loaded)
            {
                var version = (uint)Volatile.Read(ref Unsafe.As<uint, int>(ref slot.Version));
                if (version == r.Version)
                {
                    object? payload = Volatile.Read(ref slot.Payload);
                    if (payload is T p)
                    {
                        lease = new AssetLease<T>(this, r, version, p);
                        return true;
                    }
                }
            }
        }

        lease = default;
        return false;
    }

    public Task LoadAsync<T>(AssetRef<T> r, CancellationToken ct = default)
    {
        if (_store is null)
            throw new InvalidOperationException("RegisterManifest must be called before LoadAsync.");
        if (!_slots.TryGetValue(r.Id, out var slot))
            throw new KeyNotFoundException(
                $"AssetId {r.Id} not registered. Call RegisterManifest with a manifest containing this id.");

        // Volatile.Read fast path — already loaded => completed task, no lock, no alloc
        var fastState = (SlotState)Volatile.Read(ref Unsafe.As<SlotState, int>(ref slot.State));
        if (fastState == SlotState.Loaded)
        {
            var fastVersion = (uint)Volatile.Read(ref Unsafe.As<uint, int>(ref slot.Version));
            if (fastVersion == r.Version)
            {
                object? fastPayload = Volatile.Read(ref slot.Payload);
                if (fastPayload is T)
                    return Task.CompletedTask;
            }
        }

        TaskCompletionSource<bool>? tcs = null;
        Task task;

        lock (slot.Sync)
        {
            // Double-check inside lock
            if (slot.State == SlotState.Loaded && slot.Version == r.Version && slot.Payload is T)
                return Task.CompletedTask;

            if (slot.State == SlotState.Loading && slot.Inflight != null)
                return slot.Inflight;

            // Start new single-flight load
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            slot.State = SlotState.Loading;
            slot.Inflight = tcs.Task;
            task = tcs.Task;
        }

        // Outside lock: perform I/O and payload creation
        _ = LoadAsyncInner<T>(slot, tcs, ct);
        return task;
    }

    private async Task LoadAsyncInner<T>(ContentSlot slot, TaskCompletionSource<bool> tcs, CancellationToken ct)
    {
        try
        {
            var store = _store;
            if (store is null)
                throw new InvalidOperationException("Store is null during load.");

            // Perform I/O outside any lock — thread-safe per slot, not holding Sync
            var bytes = await store.GetAsync(slot.ArtifactLocator, ct).ConfigureAwait(false);

            T payload = CreatePayload<T>(bytes);

            lock (slot.Sync)
            {
                slot.Payload = payload;
                // Version 0 -> 1 on first load, then ++ on reload (retry after Failed also increments)
                if (slot.Version == 0)
                    slot.Version = 1;
                else
                    slot.Version++;
                slot.State = SlotState.Loaded;
                slot.Inflight = null;
            }

            tcs.SetResult(true);
        }
        catch (Exception ex)
        {
            lock (slot.Sync)
            {
                slot.State = SlotState.Failed;
                slot.Inflight = null;
                // Keep Payload null for Failed — retryable
            }

            tcs.SetException(ex);
        }
    }

    private static T CreatePayload<T>(ReadOnlyMemory<byte> bytes)
    {
        // Fast paths for common T to avoid deserialization overhead and allocations
        if (typeof(T) == typeof(string))
        {
            string s = System.Text.Encoding.UTF8.GetString(bytes.Span);
            return (T)(object)s;
        }

        if (typeof(T) == typeof(byte[]))
        {
            return (T)(object)bytes.ToArray();
        }

        if (typeof(T) == typeof(ReadOnlyMemory<byte>))
        {
            return (T)(object)bytes;
        }

        if (typeof(T) == typeof(RawJsonPayload))
        {
            return (T)(object)new RawJsonPayload
            {
                Json = System.Text.Encoding.UTF8.GetString(bytes.Span),
            };
        }

        // Empty payload is invalid for JSON types — fault with KCR202 (except fast paths above)
        if (bytes.Length == 0)
        {
            throw new InvalidDataException($"KCR202 Empty payload for {typeof(T).Name}");
        }

        // Trivial RawJsonPayload "{}" case is allowed to succeed (deserialize returns non-null).
        // For all other cases, JSON deserialize must succeed; failure faults with KCR202.
        try
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize<T>(bytes.Span);
            if (obj != null)
                return obj;

            throw new InvalidDataException($"KCR202 Deserialization returned null for {typeof(T).Name}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidDataException($"KCR202 Failed to deserialize {typeof(T).Name}", ex);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"KCR202 Failed to create payload {typeof(T).Name}", ex);
        }
    }
}
