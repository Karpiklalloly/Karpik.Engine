namespace Karpik.Engine.Shared;

public interface IAssetSaver
{
    public Type AssetType { get; }
    Task SaveAsync(Asset asset, Stream stream);
}