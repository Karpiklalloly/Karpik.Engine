namespace Karpik.Engine.Shared;

public struct AssetHandle<T> : IDisposable where T : Asset
{
    public T Asset { get; private set; }

    public bool IsValid => Asset is not null;
    
    private AssetsManager _manager;
    
    internal AssetHandle(T asset, AssetsManager manager)
    {
        Asset = asset;
        _manager = manager;

        Asset?.IncrementRef();
    }
    
    public void Dispose()
    {
        if (Asset != null && _manager != null)
        {
            _manager.ReleaseAsset(Asset);
            Asset = null;
            _manager = null;
        }
    }
    
    public static implicit operator T(AssetHandle<T> handle)
    {
        return handle.Asset;
    }
}