using System.Composition;
using Autofac;
using Karpik.Content.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;

namespace Karpik.Content.Runtime;

[Export(typeof(IContentRegistry))]
[Export(typeof(IContentUpdateRegistry))]
[Export(typeof(ContentRegistry))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class ContentRegistry : IContentRegistry, IContentUpdateRegistry, IStartable, IAsyncDisposable
{
    public const int MaximumAssets = 4096;
    private readonly IFileSystem _fileSystem;
    private readonly IContentStore _store;
    private readonly IContentPayloadLoader[] _loaders;
    private ContentSnapshot? _root;
    private int _updating;
    private int _disposed;
    private readonly ContentSnapshot _disposedRoot;
    private Exception? _retirementFailure;
    public ContentRegistry(IFileSystem fileSystem, IContentStore store, IEnumerable<IContentPayloadLoader>? loaders = null)
    {
        _fileSystem = fileSystem; _store = store; _loaders = loaders?.ToArray() ?? [];
        _disposedRoot = new ContentSnapshot(new(), 0, store, retainStore: false);
    }
    public void Start()
    {
        if (_store is FileContentStore { CapturedManifest: { } capturedManifest } captured)
        {
            RegisterManifest(capturedManifest, captured);
            // The snapshot and slots now retain the store; remove the service's bootstrap claim.
            captured.Dispose();
            return;
        }
        using var stream = _fileSystem.OpenRead(Path.Combine(_fileSystem.ContentPath, "manifest.json"));
        RegisterManifest(ContentManifest.Load(stream), _store);
    }
    public void RegisterManifest(ContentManifest manifest, IContentStore store)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(store);
        if (Volatile.Read(ref _root) != null) throw new InvalidOperationException("RegisterManifest is initialization-only.");
        var entries = Normalize(manifest);
        var slots = entries.ToDictionary(e => e.AssetId, e => new ContentSlot(e, 1, store));
        var snapshot = new ContentSnapshot(slots, 1, store);
        if (Interlocked.CompareExchange(ref _root, snapshot, null) != null)
        {
            foreach (var slot in slots.Values) slot.ReleaseStore();
            snapshot.ReleaseStore();
            throw new InvalidOperationException("RegisterManifest is initialization-only. Use PrepareAsync/TryCommit for updates.");
        }
    }
    public bool TryResolve<T>(AssetId id, out AssetRef<T> reference)
    {
        var root = Volatile.Read(ref _root);
        if (root != null && root.Slots.TryGetValue(id, out var slot) && (Volatile.Read(ref slot.PayloadType) is not { } bound || bound == typeof(T)))
        {
            reference = new AssetRef<T>(id, slot.Version); return true;
        }
        reference = default; return false;
    }
    public bool IsAlive<T>(AssetRef<T> reference) => IsAlive(reference.Id, reference.Version);
    public bool IsAlive(AssetId id, uint version)
    {
        var root = Volatile.Read(ref _root);
        return root != null && root.Slots.TryGetValue(id, out var slot) && slot.Version == version && Volatile.Read(ref slot.Payload) != null;
    }
    public bool TryGet<T>(AssetRef<T> reference, out AssetLease<T> lease)
    {
        var root = Volatile.Read(ref _root);
        if (root != null && root.Slots.TryGetValue(reference.Id, out var slot) && slot.Version == reference.Version && Volatile.Read(ref slot.Payload) is T value)
        {
            lease = new AssetLease<T>(this, reference, slot.Version, value); return true;
        }
        lease = default; return false;
    }
    public Task LoadAsync<T>(AssetRef<T> reference, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var root = Volatile.Read(ref _root) ?? throw new InvalidOperationException("RegisterManifest must be called first.");
        if (!root.Slots.TryGetValue(reference.Id, out var slot)) throw new KeyNotFoundException($"Unregistered asset {reference.Id}.");
        if (slot.Version != reference.Version) throw new InvalidOperationException("KCR204 Stale asset generation.");
        if (Volatile.Read(ref slot.Payload) is T) return Task.CompletedTask;
        lock (slot.Sync)
        {
            if (slot.Retired) throw new InvalidOperationException("KCR204 Retired asset generation.");
            if (slot.Payload is T) return Task.CompletedTask;
            if (slot.PayloadType != null && slot.PayloadType != typeof(T)) throw new InvalidOperationException("Asset already requested with another CLR type.");
            if (slot.Inflight != null) return slot.Inflight;
            slot.PayloadType = typeof(T);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            slot.Inflight = completion.Task;
            _ = LoadSlotAsync(slot, typeof(T), completion, ct);
            return completion.Task;
        }
    }
    private async Task LoadSlotAsync(ContentSlot slot, Type type, TaskCompletionSource completion, CancellationToken ct)
    {
        object? payload = null;
        try
        {
            var bytes = await slot.Store.GetAsync(slot.Entry.ArtifactLocator, ct).ConfigureAwait(false);
            payload = await DecodeAsync(type, slot.Entry.DeclaredType, bytes, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            lock (slot.Sync)
            {
                if (slot.Retired) throw new InvalidOperationException("KCR204 Load completed for a retired generation.");
                Volatile.Write(ref slot.Payload, payload);
                slot.Inflight = null;
            }
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            Exception failure = ex;
            Exception? cleanupFailure = null;
            try
            {
                if (payload is IContentTrackedResource { Lifetime: { } lifetime }) await lifetime.DrainAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                cleanupFailure = cleanup;
                Volatile.Write(ref _retirementFailure, cleanup);
                failure = new AggregateException(ex, cleanup);
            }
            finally
            {
                lock (slot.Sync) { slot.Inflight = null; slot.PayloadType = null; slot.RetirementFailure = cleanupFailure; }
            }
            if (failure is OperationCanceledException) completion.TrySetCanceled(ct); else completion.TrySetException(failure);
        }
    }
    private async ValueTask<object> DecodeAsync(Type type, string declared, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        foreach (var loader in _loaders)
            if (loader.CanLoad(type, declared)) return await loader.LoadAsync(type, declared, bytes, ct).ConfigureAwait(false);
        return CreatePayload(type, bytes);
    }
    public Task<PreparedContentUpdate> PrepareAsync(ContentManifest manifest, CancellationToken cancellationToken = default) =>
        PrepareCoreAsync(manifest, null, cancellationToken);
    public Task<PreparedContentUpdate> PrepareAsync(ContentManifest manifest, IContentStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        return PrepareCoreAsync(manifest, store, cancellationToken);
    }
    private async Task<PreparedContentUpdate> PrepareCoreAsync(ContentManifest manifest, IContentStore? candidateStore, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0) throw new InvalidOperationException("An update is preparing or retiring. Await its Completion first.");
        var created = new List<ContentSlot>();
        try
        {
            if (_retirementFailure != null) throw new InvalidOperationException("Previous retirement failed.", _retirementFailure);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var basis = Volatile.Read(ref _root) ?? throw new InvalidOperationException("RegisterManifest must be called first.");
            var store = candidateStore ?? basis.Store;
            var entries = Normalize(manifest);
            bool identical = entries.Length == basis.Slots.Count && entries.All(e => basis.Slots.TryGetValue(e.AssetId, out var old) && old.Entry.Equals(e));
            uint revision = identical ? basis.Revision : checked(basis.Revision + 1);
            var slots = new Dictionary<AssetId, ContentSlot>(entries.Length);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (basis.Slots.TryGetValue(entry.AssetId, out var previous) && previous.Entry.Equals(entry)) { slots.Add(entry.AssetId, previous); continue; }
                var slot = new ContentSlot(entry, revision, store);
                created.Add(slot); slots.Add(entry.AssetId, slot);
                Type? loadedType = null;
                if (previous != null) lock (previous.Sync) { if (previous.Payload != null) loadedType = previous.PayloadType; }
                if (loadedType == null) continue;
                if (previous!.Entry.DeclaredType != entry.DeclaredType) throw new NotSupportedException("Loaded declared-type migration is unsupported.");
                if (!IsBuiltin(loadedType) && !_loaders.Any(l => l.CanLoad(loadedType, entry.DeclaredType)))
                    throw new NotSupportedException("Reload of loaded custom CLR payloads requires a stable payload loader.");
                var bytes = await slot.Store.GetAsync(entry.ArtifactLocator, cancellationToken).ConfigureAwait(false);
                if (bytes.Length != entry.Size) throw new InvalidDataException("Cooked artifact size does not match manifest.");
                if (loadedType == typeof(RawJsonPayload)) using (System.Text.Json.JsonDocument.Parse(bytes)) { }
                slot.Payload = await DecodeAsync(loadedType, entry.DeclaredType, bytes, cancellationToken).ConfigureAwait(false);
                slot.PayloadType = loadedType;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var replaced = basis.Slots.Values.Where(s => !slots.TryGetValue(s.Entry.AssetId, out var n) || !ReferenceEquals(s, n)).ToArray();
            var update = new PreparedContentUpdate(this, basis, (identical ? basis : new ContentSnapshot(slots, revision, store)), replaced, created.ToArray());
            return update;
        }
        catch (Exception preparationFailure)
        {
            try { await RetireSlotsAsync(created).ConfigureAwait(false); }
            catch (Exception cleanupFailure)
            {
                Volatile.Write(ref _retirementFailure, cleanupFailure);
                throw new AggregateException(preparationFailure, cleanupFailure);
            }
            finally { Volatile.Write(ref _updating, 0); }
            throw;
        }
    }
    public bool TryCommit(PreparedContentUpdate update)
    {
        if (!ReferenceEquals(update.Owner, this) || Interlocked.CompareExchange(ref update.State, 1, 0) != 0) return false;
        bool committed = ReferenceEquals(Interlocked.CompareExchange(ref _root, update.Candidate, update.Basis), update.Basis);
        update.Committed = committed;
        ThreadPool.UnsafeQueueUserWorkItem(update, preferLocal: false);
        return committed;
    }
    internal void CompleteUpdate(PreparedContentUpdate update) => _ = CompleteUpdateAsync(update);
    private async Task CompleteUpdateAsync(PreparedContentUpdate update)
    {
        Exception? failure = null;
        try
        {
            await RetireSlotsAsync(update.Committed ? update.Replaced : update.Created).ConfigureAwait(false);
            if (!ReferenceEquals(update.Basis, update.Candidate))
                (update.Committed ? update.Basis : update.Candidate).ReleaseStore();
        }
        catch (Exception ex) { _retirementFailure = ex; failure = ex; }
        finally { Volatile.Write(ref _updating, 0); }
        if (failure == null) update.Finished.TrySetResult(); else update.Finished.TrySetException(failure);
    }
    /// <summary>Cold shutdown: first stop recording, discard/drain graphics buffers, and settle any prepared update.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0)
            throw new InvalidOperationException("Abort or commit the prepared update and await Completion before shutdown.");
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { Volatile.Write(ref _updating, 0); return; }
        var root = Interlocked.Exchange(ref _root, _disposedRoot);
        try
        {
            if (root != null)
            {
                await RetireSlotsAsync(root.Slots.Values).ConfigureAwait(false);
                root.ReleaseStore();
            }
        }
        finally { Volatile.Write(ref _updating, 0); }
    }
    private static async Task RetireSlotsAsync(IEnumerable<ContentSlot> slots)
    {
        // Close every old generation before awaiting any one slow owner/load.
        foreach (var slot in slots) lock (slot.Sync) slot.Retired = true;
        List<Exception>? errors = null;
        foreach (var slot in slots)
        {
            Task? inflight;
            object? payload;
            lock (slot.Sync) { inflight = slot.Inflight; payload = slot.Payload; }
            if (inflight != null) try { await inflight.ConfigureAwait(false); } catch { /* Inspect cleanup separately from ordinary old-load errors. */ }
            bool safeToRelease = slot.RetirementFailure == null;
            if (slot.RetirementFailure is { } loadCleanup) (errors ??= []).Add(loadCleanup);
            try
            {
                if (payload is IContentTrackedResource { Lifetime: { } lifetime }) await lifetime.DrainAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { safeToRelease = false; (errors ??= []).Add(ex); }
            finally { lock (slot.Sync) { slot.Payload = null; slot.PayloadType = null; } }
            if (safeToRelease) slot.ReleaseStore();
        }
        if (errors != null) throw new AggregateException(errors);
    }
    private static bool IsBuiltin(Type type) => type == typeof(string) || type == typeof(byte[]) || type == typeof(ReadOnlyMemory<byte>) || type == typeof(RawJsonPayload);
    private static ContentManifestEntry[] Normalize(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != ContentManifest.CurrentSchemaVersion || manifest.Entries.Count > MaximumAssets) throw new InvalidDataException("Unsupported manifest schema or asset budget exceeded.");
        var seen = new HashSet<AssetId>();
        var entries = manifest.Entries.Select(e =>
        {
            if (!seen.Add(e.AssetId) || e.Size < 0 || string.IsNullOrWhiteSpace(e.ArtifactLocator)) throw new InvalidDataException("Invalid or duplicate manifest entry.");
            return new ContentManifestEntry(e.AssetId, e.DeclaredType, e.LogicalName, e.ImportSettingsHash, e.SourceHash, e.ArtifactLocator, e.Size, e.Dependencies.OrderBy(d => d.Value).Distinct().ToArray());
        }).ToArray();
        var byId = entries.ToDictionary(e => e.AssetId);
        var states = new Dictionary<AssetId, byte>();
        void Visit(AssetId id)
        {
            if (!byId.TryGetValue(id, out var entry)) throw new InvalidDataException("Manifest dependency is missing.");
            if (states.TryGetValue(id, out byte state))
            {
                if (state == 1) throw new InvalidDataException("Manifest dependency cycle.");
                return;
            }
            states[id] = 1;
            foreach (var dependency in entry.Dependencies) Visit(dependency);
            states[id] = 2;
        }
        foreach (var entry in entries) Visit(entry.AssetId);
        return entries;
    }

    private static object CreatePayload(Type type, ReadOnlyMemory<byte> bytes)
    {
        // Fast paths for common T to avoid deserialization overhead and allocations
        if (type == typeof(string))
        {
            string s = System.Text.Encoding.UTF8.GetString(bytes.Span);
            return s;
        }

        if (type == typeof(byte[]))
        {
            return bytes.ToArray();
        }

        if (type == typeof(ReadOnlyMemory<byte>))
        {
            return bytes;
        }

        if (type == typeof(RawJsonPayload))
        {
            return new RawJsonPayload
            {
                Json = System.Text.Encoding.UTF8.GetString(bytes.Span),
            };
        }

        // Empty payload is invalid for JSON types — fault with KCR202 (except fast paths above)
        if (bytes.Length == 0)
        {
            throw new InvalidDataException($"KCR202 Empty payload for {type.Name}");
        }

        // Trivial RawJsonPayload "{}" case is allowed to succeed (deserialize returns non-null).
        // For all other cases, JSON deserialize must succeed; failure faults with KCR202.
        try
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize(bytes.Span, type);
            if (obj != null)
                return obj;

            throw new InvalidDataException($"KCR202 Deserialization returned null for {type.Name}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidDataException($"KCR202 Failed to deserialize {type.Name}", ex);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"KCR202 Failed to create payload {type.Name}", ex);
        }
    }
}
