using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

public interface IContentRegistry
{
    void RegisterManifest(ContentManifest manifest, IContentStore store);
    bool TryResolveArtifact(string logicalName, out string artifactLocator, out IContentStore store);
    bool IsAlive<T>(AssetRef<T> asset);
    bool IsAlive(AssetId id, uint version);
    bool TryGet<T>(AssetRef<T> asset, out AssetLease<T> lease);
    Task LoadAsync<T>(AssetRef<T> asset, CancellationToken ct = default);
}
