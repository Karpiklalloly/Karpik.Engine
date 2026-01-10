namespace Karpik.Engine.Shared.AssetManagement.Base;

public struct AssetHandle<T> : IDisposable where T : Asset
{
    public T? Asset { get; private set; }

    public bool IsValid => Asset is not null;
    
    private IAssetsManager? _manager;
    
    internal AssetHandle(T asset, IAssetsManager manager)
    {
        Asset = asset;
        _manager = manager;

        Asset.IncrementRef();
    }
    
    public void Dispose()
    {
        if (Asset is not null && _manager is not null)
        {
            _manager.ReleaseAsset(Asset);
            Asset = null;
            _manager = null;
        }
    }
    
    public static implicit operator T?(AssetHandle<T> handle)
    {
        return handle.Asset;
    }
}