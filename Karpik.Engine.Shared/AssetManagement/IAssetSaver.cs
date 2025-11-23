namespace Karpik.Engine.Shared;

public interface IAssetSaver
{
    Task SaveAsync(Asset asset, Stream stream);
}