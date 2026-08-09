using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.ECS;

[Export(typeof(IAssetLoader))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class ComponentsTemplateLoader : JsonLoader<ComponentsTemplateAsset, ComponentsTemplate>
{
    public override string? DefaultPath => _fileSystem.Combine(_fileSystem.ContentPath, "Player.json");
    
    private readonly IFileSystem _fileSystem;
    
    public ComponentsTemplateLoader(IFileSystem fileSystem, ILogger<ComponentArrayConverter> converterLogger)
    {
        _fileSystem = fileSystem;
        Serializer.Converters.Add(new ComponentArrayConverter(converterLogger));
    }

    protected override async JobHandle OnAssetLoadedAsync(ComponentsTemplateAsset asset, IAssetLoadContext context)
    {
        if (asset.Template.Components.Length == 0) return;

        foreach (var component in asset.Template.Components)
        {
            // TODO: Проверить
            object raw = component.GetRaw();
            if (raw is IHasDependencies hasDependencies)
            {
                foreach (var path in hasDependencies.GetDependencyPaths())
                {
                    using var handle = await context.Manager.LoadAssetByPathAsync(path);

                    if (handle.IsValid)
                    {
                        context.Manager.TryAddDependency(asset, handle.Asset);
                    }
                }
            }
        }
    }
    
    protected override ComponentsTemplateAsset EmptyAsset() => new();

    protected override void SetValue(IAssetLoadContext context, ComponentsTemplateAsset asset, ComponentsTemplate value) => asset.Template = value;
}