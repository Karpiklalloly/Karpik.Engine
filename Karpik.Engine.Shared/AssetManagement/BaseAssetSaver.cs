namespace Karpik.Engine.Shared;

public abstract class BaseAssetSaver<TAsset> : IAssetSaver where TAsset : Asset
{
    public Type AssetType => typeof(TAsset);
    
    public Task SaveAsync(Asset asset, Stream stream)
    {
        return OnSaveAsync((TAsset)asset, stream);
    }

    protected abstract Task OnSaveAsync(TAsset asset, Stream stream);
}