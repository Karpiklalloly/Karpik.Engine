namespace Karpik.Engine.Shared;

public interface IAssetLoader
{
    
    public Task<Asset> LoadAsync(Stream stream, string assetName);
}