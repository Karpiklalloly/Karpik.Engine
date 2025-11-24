namespace Karpik.Engine.Shared;

public abstract class BaseAssetLoader<T> : IAssetLoader where T : Asset
{
    public abstract string[] SupportedExtensions { get; }
    public Type AssetType => typeof(T);

    public async Task<Asset> LoadAsync(Stream stream, string assetName)
    {
        var asset = await OnLoadAsync(stream, assetName);
        await OnAssetLoadedAsync(asset);
        return asset;
    }
    
    protected abstract Task<T> OnLoadAsync(Stream stream, string assetName);
    
    protected virtual Task OnAssetLoadedAsync(T asset) => Task.CompletedTask;
}