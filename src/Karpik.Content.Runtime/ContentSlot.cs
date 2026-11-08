using Karpik.Content.Core;
namespace Karpik.Content.Runtime;
internal sealed class ContentSlot(ContentManifestEntry entry, uint version, IContentStore store)
{
    public readonly ContentManifestEntry Entry = entry;
    public readonly uint Version = version;
    public readonly IContentStore Store = ContentStoreLifetime.Retain(store);
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
    internal readonly uint Revision;
    internal readonly IContentStore Store;
    private int _storeReleased;
    internal ContentSnapshot(Dictionary<AssetId, ContentSlot> slots, uint revision, IContentStore store, bool retainStore = true)
    {
        Slots = slots; Revision = revision; Store = retainStore ? ContentStoreLifetime.Retain(store) : store;
        _storeReleased = retainStore ? 0 : 1;
    }
    internal void ReleaseStore()
    {
        if (Interlocked.Exchange(ref _storeReleased, 1) == 0) ContentStoreLifetime.Release(Store);
    }
}
