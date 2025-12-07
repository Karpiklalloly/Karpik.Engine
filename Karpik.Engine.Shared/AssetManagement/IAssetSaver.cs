namespace Karpik.Engine.Shared;

public interface IAssetSaver
{
    public Type AssetType { get; }
    JobHandle SaveAsync(Asset asset, Stream stream);
}