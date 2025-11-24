namespace Karpik.Engine.Shared;

public class ComponentsTemplateLoader : JsonLoader<ComponentsTemplate>
{
    public ComponentsTemplateLoader()
    {
        Serializer.Converters.Add(new ComponentArrayConverter());
    }

    protected override async Task OnAssetLoadedAsync(ComponentsTemplate asset)
    {
        var loads = asset.Components
            .Where(x => x.Type.IsAssignableTo(typeof(IEcsComponentOnLoad)))
            .Select(x => x.GetRaw())
            .Cast<IEcsComponentOnLoad>()
            .ToAsyncEnumerable();
        await foreach (var load in loads)
        {
            await load.OnLoad(AssetsManager);
        }
    }
}