using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

public interface IContentRegistry
{
    void RegisterManifest(ContentManifest manifest, IContentStore store);
    /// <summary>Resolves a borrowed store. Use TryOpenArtifact for reads that may race publication.</summary>
    bool TryResolveArtifact(string logicalName, out string artifactLocator, out IContentStore store)
    {
        artifactLocator = null!;
        store = null!;
        return false;
    }
    Stream? TryOpenArtifact(string logicalName) =>
        TryResolveArtifact(logicalName, out string locator, out IContentStore store) ? store.OpenRead(locator) : null;
    bool IsAlive<T>(AssetRef<T> asset);
    bool IsAlive(AssetId id, uint version);
    bool TryGet<T>(AssetRef<T> asset, out AssetLease<T> lease);
    Task LoadAsync<T>(AssetRef<T> asset, CancellationToken ct = default);
}
