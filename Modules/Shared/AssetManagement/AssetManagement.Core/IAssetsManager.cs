namespace Karpik.Engine.Shared.AssetManagement.Core;

public interface IAssetsManager : IDisposable
{
    public string RootPath { get; }
    public string ContentPath { get; }
    public string ModsPath { get; }
    public IFileSystem FileSystem { get; }

    public void RegisterSaver(IAssetSaver saver);
    public void RegisterLoader(IAssetLoader loader);

    public JobHandle<AssetHandle<T>> LoadAssetAsync<T>(string path) where T : Asset;
    public JobHandle<AssetHandle<Asset>> LoadAssetByPathAsync(string path);
    public JobHandle<AssetHandle<T>> SaveAssetAsync<T>(T asset, string? path = null) where T : Asset;
    
    protected internal void ReleaseAsset(Asset asset);
}