namespace Karpik.Engine.Shared.AssetManagement.Core;

public interface IAssetLoadContext
{
    public IAssetsManager Manager { get; }
}

// TODO: подумать про структуру, а не класс
public class AssetLoadContext(IAssetsManager manager) : IAssetLoadContext
{
    public IAssetsManager Manager { get; } = manager;
}