namespace Karpik.Engine.Shared;

public interface IAssetLoader
{
    public string[] SupportedExtensions { get; }
    public Task<Asset> LoadAsync(Stream stream, string assetName);
}