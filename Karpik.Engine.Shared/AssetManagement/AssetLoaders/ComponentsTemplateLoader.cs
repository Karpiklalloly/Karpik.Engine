using Karpik.Engine.Shared.AssetManagement;

namespace Karpik.Engine.Shared;

public class ComponentsTemplateLoader : JsonLoader<ComponentsTemplateAsset, ComponentsTemplate>
{
    public ComponentsTemplateLoader()
    {
        Serializer.Converters.Add(new ComponentArrayConverter());
    }

    protected override Task OnAssetLoadedAsync(ComponentsTemplateAsset asset)
    {
        asset.Template.OnLoad(AssetsManager);
        return Task.CompletedTask;
    }

    protected override ComponentsTemplateAsset EmptyAsset() => new();

    protected override void SetValue(ComponentsTemplateAsset asset, ComponentsTemplate value) => asset.Template = value;
}