using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Shared.ECS;

[Export(typeof(IAssetSaver))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class ComponentsTemplateSaver : JsonSaver<ComponentsTemplateAsset>
{
    public ComponentsTemplateSaver()
    {
        Serializer.Converters.Add(new ComponentArrayConverter());
    }
}