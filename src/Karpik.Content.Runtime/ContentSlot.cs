using Karpik.Content.Core;
namespace Karpik.Content.Runtime;
internal sealed class ContentSlot(ContentManifestEntry entry, uint version, IContentStore store, string? modId = null)
{
    public readonly ContentManifestEntry Entry = entry;
    public readonly uint Version = version;
    public readonly IContentStore Store = ContentStoreLifetime.Retain(store);
    internal readonly string? ModId = modId;
    // Matching base descriptors keep their historical slot bindings even when
    // the default base store changes. Mod sources must match both owner and store.
    internal bool Matches(ContentSource source) => ModId == source.ModId &&
        (ModId == null || ReferenceEquals(Store, source.Store)) && Entry.Equals(source.Entry);

    private int _storeReleased;
    internal void ReleaseStore()
    {
        if (Interlocked.Exchange(ref _storeReleased, 1) == 0) ContentStoreLifetime.Release(Store);
    }
    public readonly object Sync = new();
    public object? Payload;
    public Type? PayloadType;
    public Task? Inflight;
    public bool Retired;
    public Exception? RetirementFailure;
}
internal sealed class ContentSnapshot
{
    internal readonly Dictionary<AssetId, ContentSlot> Slots;
    internal readonly Dictionary<string, ContentManifestEntry?> Artifacts;
    internal readonly ContentManifestEntry[] BaseEntries;
    internal readonly IReadOnlyList<ModContentPackage> Packages;
    internal readonly IReadOnlyCollection<string> InstalledModIds;
    internal readonly uint Revision;
    internal readonly IContentStore Store;
    private bool _storeReleased;

    internal ContentSnapshot(Dictionary<AssetId, ContentSlot> slots, uint revision, IContentStore store,
        ContentManifestEntry[] baseEntries, IReadOnlyList<ModContentPackage>? packages = null,
        IReadOnlyCollection<string>? installedModIds = null, bool retainStore = true)
    {
        Slots = slots;
        BaseEntries = baseEntries;
        Packages = packages ?? [];
        InstalledModIds = installedModIds ?? [];
        Artifacts = new(baseEntries.Length, StringComparer.Ordinal);
        foreach (var entry in baseEntries)
        {
            string name = entry.LogicalName;
            if (!string.IsNullOrEmpty(name) && !Artifacts.TryAdd(name, entry))
                Artifacts[name] = null; // Ambiguous names remain addressable by AssetId.
        }
        Revision = revision;
        Store = retainStore ? ContentStoreLifetime.Retain(store) : store;
        _storeReleased = !retainStore;
        int retained = 0;
        try
        {
            for (; retained < Packages.Count; retained++) ContentStoreLifetime.Retain(Packages[retained].Store);
        }
        catch
        {
            ReleaseClaims(retained);
            throw;
        }
    }

    // Only the legacy stream-opening API needs a temporary claim. TryResolve
    // and TryGet still use their original lock-free single lookup.
    internal bool TryRetainStore()
    {
        lock (this)
        {
            if (_storeReleased) return false;
            ContentStoreLifetime.Retain(Store);
            return true;
        }
    }

    internal void ReleaseStore()
    {
        lock (this)
        {
            if (_storeReleased) return;
            _storeReleased = true;
        }
        ReleaseClaims(Packages.Count);
    }

    private void ReleaseClaims(int packageCount)
    {
        List<Exception>? errors = null;
        void Release(IContentStore store)
        {
            try { ContentStoreLifetime.Release(store); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        Release(Store);
        for (int i = 0; i < packageCount; i++) Release(Packages[i].Store);
        if (errors != null) throw new AggregateException(errors);
    }
}
