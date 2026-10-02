using System.Buffers;

namespace Karpik.Engine.Shared.AssetManagement.Core;

public abstract class BaseAssetLoader<TAsset, TValue>
    : IAssetLoader
    where TAsset : Asset
{
    public abstract string? DefaultPath { get; }
    
    public abstract string[] SupportedExtensions { get; }
    
    public Type AssetType => typeof(TAsset);
    
    protected ArrayPool<byte> ArrayPool { get; } = ArrayPool<byte>.Shared;

    public async JobHandle<Asset> LoadAsync(IAssetLoadContext context, Stream stream, string assetName)
    {
        var value = await OnLoadAsync(context, stream, assetName);
        var asset = EmptyAsset();
        if (value is null)
        {
            return asset;
        }
        
        SetValue(context, asset, value);
        await OnAssetLoadedAsync(asset, context);
        return asset;
    }
    
    protected abstract JobHandle<TValue?> OnLoadAsync(IAssetLoadContext context, Stream stream, string assetName);
    protected abstract TAsset EmptyAsset();
    protected abstract void SetValue(IAssetLoadContext context, TAsset asset, TValue value);
    
    protected virtual JobHandle OnAssetLoadedAsync(TAsset asset, IAssetLoadContext context) => JobHandle.Completed;
}