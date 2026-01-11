namespace Karpik.Engine.Shared.AssetManagement.Core;

public interface IAssetSaver
{
    public Type AssetType { get; }
    JobHandle SaveAsync(Asset asset, Stream stream);
}