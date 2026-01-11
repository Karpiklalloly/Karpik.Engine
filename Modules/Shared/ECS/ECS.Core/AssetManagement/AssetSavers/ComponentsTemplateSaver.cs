using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Shared.ECS;

public class ComponentsTemplateSaver : JsonSaver<ComponentsTemplateAsset>
{
    public ComponentsTemplateSaver()
    {
        Serializer.Converters.Add(new ComponentArrayConverter());
    }
}