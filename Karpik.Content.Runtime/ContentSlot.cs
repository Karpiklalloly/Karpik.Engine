using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

internal enum SlotState
{
    Unloaded,
    Loading,
    Loaded,
    Failed
}

internal sealed class ContentSlot
{
    public AssetId Id;
    public SlotState State;
    public uint Version;
    public object? Payload;
    public AssetId[] Dependencies = [];
    public Task? Inflight;
    public object Sync = new();
    public string ArtifactLocator = "";
    public string DeclaredType = "";
}
