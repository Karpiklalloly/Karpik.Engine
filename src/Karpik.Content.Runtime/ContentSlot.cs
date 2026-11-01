using Karpik.Content.Core;
namespace Karpik.Content.Runtime;
internal sealed class ContentSlot(ContentManifestEntry entry, uint version, IContentStore store)
{
    public readonly ContentManifestEntry Entry = entry;
    public readonly uint Version = version;
    public readonly IContentStore Store = store;
    public readonly object Sync = new();
    public object? Payload;
    public Type? PayloadType;
    public Task? Inflight;
    public bool Retired;
    public Exception? RetirementFailure;
}
internal sealed record ContentSnapshot(Dictionary<AssetId, ContentSlot> Slots, uint Revision, IContentStore Store);
