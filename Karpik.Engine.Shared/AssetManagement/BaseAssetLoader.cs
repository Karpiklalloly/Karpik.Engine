namespace Karpik.Engine.Shared;

public abstract class BaseAssetLoader<TAsset, TValue> : IAssetLoader where TAsset : Asset
{
    public abstract string[] SupportedExtensions { get; }
    public Type AssetType => typeof(TAsset);
    [DI] protected MainTreadScheduler MainTreadScheduler { get; set; }

    public async Task<Asset> LoadAsync(Stream stream, string assetName)
    {
        var value = await OnLoadAsync(stream, assetName);
        var asset = EmptyAsset();
        SetValue(asset, value);
        await OnAssetLoadedAsync(asset);
        return asset;
    }
    
    protected abstract Task<TValue> OnLoadAsync(Stream stream, string assetName);
    protected abstract TAsset EmptyAsset();
    protected abstract void SetValue(TAsset asset, TValue value);
    
    protected virtual Task OnAssetLoadedAsync(TAsset asset) => Task.CompletedTask;
}