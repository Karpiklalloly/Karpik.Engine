using System.Buffers;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.AssetManagement.Core;

public abstract class BaseAssetLoader<TAsset, TValue> : IAssetLoader where TAsset : Asset
{
    public abstract string? DefaultPath { get; }
    public abstract string[] SupportedExtensions { get; }
    public Type AssetType => typeof(TAsset);
    [DI] protected MainThreadScheduler MainThreadScheduler { get; private set; }
    [DI] protected IAssetsManager AssetsManager { get; private set; }
    [DI] protected IServiceContainer ServiceContainer { get; private set; }
    protected ArrayPool<byte> ArrayPool { get; } = ArrayPool<byte>.Shared;

    public async JobHandle<Asset> LoadAsync(Stream stream, string assetName)
    {
        var value = await OnLoadAsync(stream, assetName);
        var asset = EmptyAsset();
        if (value is null)
        {
            return asset;
        }
        
        SetValue(asset, value);
        await OnAssetLoadedAsync(asset);
        return asset;
    }
    
    protected abstract JobHandle<TValue?> OnLoadAsync(Stream stream, string assetName);
    protected abstract TAsset EmptyAsset();
    protected abstract void SetValue(TAsset asset, TValue value);
    
    protected virtual JobHandle OnAssetLoadedAsync(TAsset asset) => JobHandle.Completed;
}