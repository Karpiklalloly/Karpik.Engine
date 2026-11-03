using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

/// <summary>One cold preparation and one latest pending update; publication belongs to the host's simulation boundary.</summary>
public sealed class ContentReloadCoordinator : IContentChangeSource, IContentReloadLifetime, IAsyncDisposable
{
    public const int MaximumChanges = ContentRegistry.MaximumAssets * 2;
    private readonly ContentRegistry _registry;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly ContentAssetChange[] _staged = new ContentAssetChange[MaximumChanges];
    private readonly ContentAssetChange[] _published = new ContentAssetChange[MaximumChanges];
    private readonly IComparer<ContentAssetChange> _changeOrder = Comparer<ContentAssetChange>.Create(static (a, b) => a.AssetId.CompareTo(b.AssetId));
    private readonly Task _worker;
    private Task _stopCallbacks = Task.CompletedTask;
    private Task? _disposal;
    private Request? _pending;
    private Request? _active;
    private Publication? _ready;
    private Exception? _failure;
    private int _stopping;
    private int _publishedCount;
    private long _sequence;

    public ContentReloadCoordinator(ContentRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _worker = Task.Run(RunAsync);
    }

    /// <summary>Borrowed dense buffer, valid on the simulation thread until its next PublishBoundary call.</summary>
    public ReadOnlySpan<ContentAssetChange> Changes => _published.AsSpan(0, Volatile.Read(ref _publishedCount));
    public ulong Sequence => unchecked((ulong)Volatile.Read(ref _sequence));
    /// <summary>Sticky retirement/control-worker failure. A previously applied update remains applied.</summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    public Task<ContentReloadResult> EnqueueAsync(ContentManifest manifest, IContentStore store)
    {
        Request request;
        try
        {
            ArgumentNullException.ThrowIfNull(store);
            request = new Request(Freeze(manifest), ContentStoreLifetime.Retain(store));
        }
        catch (Exception error)
        {
            return Task.FromResult(new ContentReloadResult(ContentReloadOutcome.Failed, error.Message));
        }
        Request? superseded = null;
        bool accepted;
        lock (_sync)
        {
            accepted = _stopping == 0 && _failure == null;
            if (accepted)
            {
                superseded = _pending;
                _pending = request;
                if (superseded == null) _wake.Release();
            }
            else request.Finish(ContentReloadOutcome.Failed, _failure?.Message ?? "Content reload is stopping.");
        }
        if (!accepted) Release(request);
        if (superseded != null)
        {
            superseded.Finish(ContentReloadOutcome.Superseded);
            Release(superseded);
        }
        return request.Result.Task;
    }

    /// <summary>No allocation, wait, lock, I/O or completion callbacks. Call once at a quiescent simulation boundary.</summary>
    public bool PublishBoundary()
    {
        Volatile.Write(ref _publishedCount, 0);
        var ready = Volatile.Read(ref _ready);
        if (ready == null || Volatile.Read(ref _stopping) != 0 ||
            Interlocked.CompareExchange(ref ready.State, Publication.Committing, Publication.Waiting) != Publication.Waiting) return false;
        bool committed = _registry.TryCommit(ready.Update);
        if (committed)
        {
            Array.Copy(_staged, _published, ready.Count);
            Volatile.Write(ref _publishedCount, ready.Count);
            if (ready.Count != 0) Interlocked.Increment(ref _sequence);
        }
        Volatile.Write(ref ready.State, committed ? Publication.Committed : Publication.Rejected);
        return committed;
    }

    /// <summary>Settle delivery requests without waiting for preparation, renderer claims or backend retirement.</summary>
    public void RequestStop()
    {
        Publication? abort = null;
        lock (_sync)
        {
            if (_stopping != 0) return;
            Volatile.Write(ref _stopping, 1);
            _pending?.Finish(ContentReloadOutcome.Failed, "Content reload stopped before application.");
            var ready = Volatile.Read(ref _ready);
            if (ready != null && ReferenceEquals(ready.Request, _active))
            {
                int state = Interlocked.CompareExchange(ref ready.State, Publication.Aborted, Publication.Waiting);
                if (state == Publication.Waiting)
                {
                    abort = ready;
                    _active?.Finish(ContentReloadOutcome.Failed, "Content reload stopped before application.");
                }
                else if (state == Publication.Committed) _active?.Finish(ContentReloadOutcome.Applied);
                else if (state != Publication.Committing) _active?.Finish(ContentReloadOutcome.Failed, "Content reload stopped before application.");
                // A boundary already owning Committing wins; the worker acknowledges it after its atomic publication.
            }
            else _active?.Finish(ContentReloadOutcome.Failed, "Content reload stopped before application.");
            // CancelAsync signals cancellation immediately while running arbitrary decoder callbacks elsewhere.
            _stopCallbacks = _stop.CancelAsync();
        }
        abort?.Update.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        RequestStop();
        lock (_sync) return new ValueTask(_disposal ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        await _worker.ConfigureAwait(false);
        await _stopCallbacks.ConfigureAwait(false);
        _wake.Dispose();
        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await _wake.WaitAsync(_stop.Token).ConfigureAwait(false);
                Request? request;
                lock (_sync)
                {
                    if (_stopping != 0) break;
                    request = _pending;
                    _pending = null;
                    _active = request;
                }
                if (request == null) continue;
                await ProcessAsync(request).ConfigureAwait(false);
                lock (_sync) _active = null;
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            Volatile.Write(ref _failure, error);
            RequestStop();
        }
        finally
        {
            Request? pending;
            Request? active;
            lock (_sync) { pending = _pending; _pending = null; active = _active; _active = null; }
            pending?.Finish(ContentReloadOutcome.Failed, "Content reload stopped before application.");
            active?.Finish(ContentReloadOutcome.Failed, "Content reload stopped before application.");
            if (pending != null) Release(pending);
            if (active != null) Release(active);
        }
    }

    private async Task ProcessAsync(Request request)
    {
        PreparedContentUpdate? update = null;
        Publication? publication = null;
        try
        {
            if (Failure is { } prior) throw new InvalidOperationException("Previous Content retirement failed.", prior);
            update = await _registry.PrepareAsync(request.Manifest, request.Store, _stop.Token).ConfigureAwait(false);
            Release(request); // Prepared snapshot/slots now own every store they actually use.
            int count = StageChanges(update);
            publication = new Publication(request, update, count);
            lock (_sync)
            {
                if (_stopping != 0) publication.State = Publication.Aborted;
                else Volatile.Write(ref _ready, publication);
            }
            while (Volatile.Read(ref publication.State) is Publication.Waiting or Publication.Committing)
                await Task.Delay(1).ConfigureAwait(false);
            if (Volatile.Read(ref publication.State) == Publication.Committed)
                request.Finish(ContentReloadOutcome.Applied);
            else
            {
                request.Finish(ContentReloadOutcome.Failed, "Content reload was stopped or its publication basis was no longer current.");
                update.Dispose();
            }
        }
        catch (Exception error)
        {
            request.Finish(ContentReloadOutcome.Failed, error.Message);
            update?.Dispose();
        }
        finally
        {
            // Applied is acknowledged before retirement, including when renderer claims delay cleanup.
            if (update != null)
            {
                try { await update.Completion.ConfigureAwait(false); }
                catch (Exception error) { Volatile.Write(ref _failure, error); }
            }
            if (publication != null) Interlocked.CompareExchange(ref _ready, null, publication);
            Release(request);
        }
    }

    private int StageChanges(PreparedContentUpdate update)
    {
        int count = 0;
        foreach (var pair in update.Candidate.Slots)
        {
            if (!update.Basis.Slots.TryGetValue(pair.Key, out var previous))
                _staged[count++] = new(pair.Key, ContentAssetChangeKind.Added);
            else if (!ReferenceEquals(previous, pair.Value))
                _staged[count++] = new(pair.Key, ContentAssetChangeKind.Updated);
        }
        foreach (var pair in update.Basis.Slots)
            if (!update.Candidate.Slots.ContainsKey(pair.Key)) _staged[count++] = new(pair.Key, ContentAssetChangeKind.Removed);
        Array.Sort(_staged, 0, count, _changeOrder);
        return count;
    }

    private static ContentManifest Freeze(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != ContentManifest.CurrentSchemaVersion || manifest.Entries == null || manifest.Entries.Count > ContentRegistry.MaximumAssets)
            throw new InvalidDataException("Unsupported Content manifest schema or asset budget.");
        var entries = new ContentManifestEntry[manifest.Entries.Count];
        for (int index = 0; index < entries.Length; index++)
        {
            var entry = manifest.Entries[index];
            if (entry == null || entry.Dependencies == null || entry.Dependencies.Count > ContentRegistry.MaximumAssets)
                throw new InvalidDataException("Invalid Content entry/dependency budget.");
            entries[index] = new(entry.AssetId, entry.DeclaredType, entry.LogicalName, entry.ImportSettingsHash,
                entry.SourceHash, entry.ArtifactLocator, entry.Size, entry.Dependencies.ToArray());
        }
        return new ContentManifest(manifest.SchemaVersion, entries);
    }

    private void Release(Request request)
    {
        if (Interlocked.Exchange(ref request.StoreReleased, 1) != 0) return;
        try { ContentStoreLifetime.Release(request.Store); }
        catch (Exception error) { Volatile.Write(ref _failure, error); }
    }

    private sealed class Request(ContentManifest manifest, IContentStore store)
    {
        internal readonly ContentManifest Manifest = manifest;
        internal readonly IContentStore Store = store;
        internal readonly TaskCompletionSource<ContentReloadResult> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int StoreReleased;
        internal void Finish(ContentReloadOutcome outcome, string? error = null) => Result.TrySetResult(new(outcome, error));
    }
    private sealed class Publication(Request request, PreparedContentUpdate update, int count)
    {
        internal const int Waiting = 0, Committing = 1, Committed = 2, Rejected = 3, Aborted = 4;
        internal readonly Request Request = request;
        internal readonly PreparedContentUpdate Update = update;
        internal readonly int Count = count;
        internal int State;
    }
}
