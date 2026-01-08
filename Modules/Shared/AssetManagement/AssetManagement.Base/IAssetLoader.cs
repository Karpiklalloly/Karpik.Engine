namespace Karpik.Engine.Shared.AssetManagement.Base;

public interface IAssetLoader
{
    public string? DefaultPath { get; }
    public string[] SupportedExtensions { get; }
    
    public Type AssetType { get; }
    
    public JobHandle<Asset> LoadAsync(Stream stream, string assetName);
}