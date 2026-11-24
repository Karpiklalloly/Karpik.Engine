using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

/// <summary>Serializes cold base reloads and mod refreshes; publication belongs to the host's simulation boundary.</summary>
/// <remarks>Finalize registry startup before accepting requests. Use this coordinator as the sole updater and dispose it before the registry.</remarks>
public sealed class ContentReloadCoordinator : IContentChangeSource, IContentReloadLifetime, IAsyncDisposable
{
    public const int MaximumChanges = ContentRegistry.MaximumAssets * 2;
    private const string StoppedBeforeApplication = "Content reload stopped before application.";
    private readonly ContentRegistry _registry;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _wake = new(0, 2);
    private readonly CancellationTokenSource _stop = new();
    private readonly ContentAssetChange[] _staged = new ContentAssetChange[MaximumChanges];
    private readonly ContentAssetChange[] _published = new ContentAssetChange[MaximumChanges];
    private readonly IComparer<ContentAssetChange> _changeOrder = Comparer<ContentAssetChange>.Create(static (a, b) => a.AssetId.CompareTo(b.AssetId));
    private readonly Task _worker;
    private Task _stopCallbacks = Task.CompletedTask;
    private Task? _disposal;
    private BaseRequest? _pendingBase;
    private ModRequest? _pendingMod;
    private bool _baseFirst;
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

    public Task<ContentReloadResult> EnqueueAsync(ContentManifest manifest, IContentStore store) =>
        EnqueueAsync(manifest, store, null);

    /// <summary>Capture the next ordered mod layer on the cold worker; the callback borrows the latest committed packages for the duration of its task.</summary>
    /// <remarks>
    /// Return selected packages (including any reused previous packages), installed IDs independently of their content,
    /// and every owned capture wrapper. On successful return the coordinator owns and releases those wrappers;
    /// on exception the callback owns partial cleanup. Do not mutate the returned lists or keep using borrowed stores after return.
    /// </remarks>
    public Task<ContentReloadResult> EnqueueModRefreshAsync(
        Func<IReadOnlyList<ModContentPackage>, CancellationToken, Task<(IReadOnlyList<ModContentPackage> Packages,
            IReadOnlyCollection<string> InstalledModIds, IReadOnlyList<ModContentCapture> Captures)>> capture,
        Action<ContentDiagnostic>? report = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        lock (_sync)
        {
            var failure = Failure;
            if (_stopping != 0 || failure != null)
                return Task.FromResult(new ContentReloadResult(ContentReloadOutcome.Failed, failure?.Message ?? "Content reload is stopping."));
            if (_pendingMod != null || IsModPreparing())
                return Task.FromResult(new ContentReloadResult(ContentReloadOutcome.Busy));

            var request = new ModRequest(capture, report);
            _pendingMod = request;
            _baseFirst = true; // Mod appends after any pending base.
            _wake.Release();
            return request.Result.Task;
        }
    }

    private bool IsModPreparing()
    {
        if (_active is not ModRequest active) return false;
        // A winning boundary opens admission before the worker acknowledges it.
        // If the worker has already cleared _ready, it has also completed Result.
        var ready = Volatile.Read(ref _ready);
        return (ready == null || Volatile.Read(ref ready.State) != Publication.Committed) && !active.Result.Task.IsCompleted;
    }

    public Task<ContentReloadResult> EnqueueAsync(ContentManifest manifest, IContentStore store, Action<ContentDiagnostic>? report)
    {
        BaseRequest request;
        try
        {
            ArgumentNullException.ThrowIfNull(store);
            request = new BaseRequest(Freeze(manifest), ContentStoreLifetime.Retain(store), report);
        }
        catch (Exception error)
        {
            return Task.FromResult(new ContentReloadResult(ContentReloadOutcome.Failed, error.Message));
        }
        BaseRequest? superseded = null;
        bool accepted;
        lock (_sync)
        {
            var failure = Failure;
            accepted = _stopping == 0 && failure == null;
            if (accepted)
            {
                superseded = _pendingBase;
                _pendingBase = request;
                _baseFirst = false; // Replacement also appends after any pending mod.
                if (superseded == null) _wake.Release();
            }
            else request.Finish(ContentReloadOutcome.Failed, failure?.Message ?? "Content reload is stopping.");
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
            _pendingBase?.Finish(ContentReloadOutcome.Failed, StoppedBeforeApplication);
            _pendingMod?.Finish(ContentReloadOutcome.Failed, StoppedBeforeApplication);
            var ready = Volatile.Read(ref _ready);
            if (ready != null && ReferenceEquals(ready.Request, _active))
            {
                int state = Interlocked.CompareExchange(ref ready.State, Publication.Aborted, Publication.Waiting);
                if (state == Publication.Waiting)
                {
                    abort = ready;
                    _active?.Finish(ContentReloadOutcome.Failed, StoppedBeforeApplication);
                }
                else if (state == Publication.Committed) _active?.Finish(ContentReloadOutcome.Applied);
                else if (state != Publication.Committing) _active?.Finish(ContentReloadOutcome.Failed, StoppedBeforeApplication);
                // A boundary already owning Committing wins; the worker acknowledges it after its atomic publication.
            }
            else _active?.Finish(ContentReloadOutcome.Failed, StoppedBeforeApplication);
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
                    if (_pendingMod != null && (_pendingBase == null || !_baseFirst))
                    {
                        request = _pendingMod;
                        _pendingMod = null;
                    }
                    else
                    {
                        request = _pendingBase;
                        _pendingBase = null;
                    }
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
            BaseRequest? pendingBase;
            Request? active;
            lock (_sync)
            {
                pendingBase = _pendingBase; _pendingBase = null;
                _pendingMod = null; // RequestStop already settled it; no capture ran.
                active = _active; _active = null;
            }
            if (pendingBase != null) Release(pendingBase);
            if (active is BaseRequest activeBase) Release(activeBase);
        }
    }

    private async Task ProcessAsync(Request request)
    {
        PreparedContentUpdate? update = null;
        Publication? publication = null;
        try
        {
            if (Failure is { } prior) throw new InvalidOperationException("Previous Content retirement failed.", prior);
            var cancellation = _stop.Token;
            cancellation.ThrowIfCancellationRequested();
            if (request is ModRequest mod)
            {
                var batch = await mod.Capture(_registry.CommittedSnapshot.Packages, cancellation).ConfigureAwait(false);
                Exception? releaseFailure = null;
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    update = await _registry.PrepareModUpdateAsync(batch.Packages, batch.InstalledModIds, mod.Report, cancellation).ConfigureAwait(false);
                }
                finally
                {
                    // If preparation succeeded, the candidate owns every store it uses.
                    releaseFailure = ReleaseCaptures(batch.Captures);
                }
                if (releaseFailure != null) throw new InvalidOperationException(releaseFailure.Message, releaseFailure);
            }
            else
            {
                var baseRequest = (BaseRequest)request;
                update = await _registry.PrepareAsync(baseRequest.Manifest, baseRequest.Store, baseRequest.Report, cancellation).ConfigureAwait(false);
                Release(baseRequest);
            }
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
            if (request is BaseRequest baseRequest) Release(baseRequest);
        }
    }

    private Exception? ReleaseCaptures(IReadOnlyList<ModContentCapture> captures)
    {
        Exception? firstError = null;
        for (int i = 0; i < captures.Count; i++)
        {
            try { captures[i].Dispose(); }
            catch (Exception error) { firstError ??= error; }
        }
        if (firstError != null) Volatile.Write(ref _failure, firstError);
        return firstError;
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

    private void Release(BaseRequest request)
    {
        if (Interlocked.Exchange(ref request.StoreReleased, 1) != 0) return;
        try { ContentStoreLifetime.Release(request.Store); }
        catch (Exception error) { Volatile.Write(ref _failure, error); }
    }

    private abstract class Request(Action<ContentDiagnostic>? report)
    {
        internal readonly Action<ContentDiagnostic>? Report = report;
        internal readonly TaskCompletionSource<ContentReloadResult> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Finish(ContentReloadOutcome outcome, string? error = null) => Result.TrySetResult(new(outcome, error));
    }
    private sealed class BaseRequest(ContentManifest manifest, IContentStore store, Action<ContentDiagnostic>? report) : Request(report)
    {
        internal readonly ContentManifest Manifest = manifest;
        internal readonly IContentStore Store = store;
        internal int StoreReleased;
    }
    private sealed class ModRequest(
        Func<IReadOnlyList<ModContentPackage>, CancellationToken, Task<(IReadOnlyList<ModContentPackage> Packages,
            IReadOnlyCollection<string> InstalledModIds, IReadOnlyList<ModContentCapture> Captures)>> capture,
        Action<ContentDiagnostic>? report) : Request(report)
    {
        internal readonly Func<IReadOnlyList<ModContentPackage>, CancellationToken, Task<(IReadOnlyList<ModContentPackage> Packages,
            IReadOnlyCollection<string> InstalledModIds, IReadOnlyList<ModContentCapture> Captures)>> Capture = capture;
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
