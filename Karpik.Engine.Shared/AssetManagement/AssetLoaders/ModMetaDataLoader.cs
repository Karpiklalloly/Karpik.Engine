using Karpik.Engine.Shared.Modding;

namespace Karpik.Engine.Shared;

public class ModMetaDataLoader : JsonLoader<ModMetaDataAsset, ModMetaData>
{
    public override string DefaultPath => null;
    protected override ModMetaDataAsset EmptyAsset() => new();

    protected override void SetValue(ModMetaDataAsset asset, ModMetaData value) => asset.MetaData = value;
}