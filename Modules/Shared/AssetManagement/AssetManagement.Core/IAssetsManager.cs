namespace Karpik.Engine.Shared.AssetManagement.Core;

public interface IAssetsManager : IDisposable
{
    public void RegisterSaver(IAssetSaver saver);
    public void RegisterLoader(IAssetLoader loader);

    public JobHandle<AssetHandle<T>> LoadAssetAsync<T>(string path) where T : Asset;
    public JobHandle<AssetHandle<Asset>> LoadAssetByPathAsync(string path);
    public JobHandle<AssetHandle<T>> SaveAssetAsync<T>(T asset, string? path = null) where T : Asset;

    public bool TryAddDependency(Asset? parent, Asset? child);
    
    protected internal void ReleaseAsset(Asset asset);
}
