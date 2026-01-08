using Karpik.Engine.Shared.AssetManagement.Base;

namespace Karpik.Engine.Shared.Modding;

public class ModMetaDataLoader : JsonLoader<ModMetaDataAsset, ModMetaData>
{
    public override string? DefaultPath => null;
    protected override ModMetaDataAsset EmptyAsset() => new();

    protected override void SetValue(ModMetaDataAsset asset, ModMetaData value) => asset.MetaData = value;
}