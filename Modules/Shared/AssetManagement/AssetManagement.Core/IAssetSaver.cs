namespace Karpik.Engine.Shared.AssetManagement.Base;

public interface IAssetSaver
{
    public Type AssetType { get; }
    JobHandle SaveAsync(Asset asset, Stream stream);
}