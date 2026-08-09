using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.ECS;

[Export(typeof(IAssetSaver))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class ComponentsTemplateSaver : JsonSaver<ComponentsTemplateAsset>
{
    public ComponentsTemplateSaver(ILogger<ComponentArrayConverter> logger)
    {
        Serializer.Converters.Add(new ComponentArrayConverter(logger));
    }
}