using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Shared.Modding;

[Export(typeof(IAssetLoader))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class ModMetaDataLoader : JsonLoader<ModMetaDataAsset, ModMetaData>
{
    public override string? DefaultPath => null;
    protected override ModMetaDataAsset EmptyAsset() => new();

    protected override void SetValue(IAssetLoadContext context, ModMetaDataAsset asset, ModMetaData value) => asset.MetaData = value;
}