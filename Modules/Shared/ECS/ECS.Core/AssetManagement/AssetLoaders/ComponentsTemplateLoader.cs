using Karpik.Engine.Shared.AssetManagement.Base;
using Karpik.Jobs;

namespace Karpik.Engine.Shared.ECS;

public class ComponentsTemplateLoader : JsonLoader<ComponentsTemplateAsset, ComponentsTemplate>
{
    public override string? DefaultPath => AssetsManager.FileSystem.Combine(AssetsManager.ContentPath, "Player.json");
    
    public ComponentsTemplateLoader()
    {
        Serializer.Converters.Add(new ComponentArrayConverter());
    }

    protected override async JobHandle OnAssetLoadedAsync(ComponentsTemplateAsset asset)
    {
        asset.Template.OnLoad(AssetsManager);
        
        if (asset.Template.Components is null || asset.Template.Components.Length == 0) return;

        foreach (var component in asset.Template.Components)
        {
            // TODO: Проверить
            object raw = component.GetRaw();
            if (raw is IHasDependencies hasDependencies)
            {
                foreach (var path in hasDependencies.GetDependencyPaths())
                {
                    using var handle = await AssetsManager.LoadAssetByPathAsync(path);

                    if (handle.IsValid)
                    {
                        asset.AddDependency(handle.Asset);
                    }
                }
            }
        }
    }
    
    protected override ComponentsTemplateAsset EmptyAsset() => new();

    protected override void SetValue(ComponentsTemplateAsset asset, ComponentsTemplate value) => asset.Template = value;
}