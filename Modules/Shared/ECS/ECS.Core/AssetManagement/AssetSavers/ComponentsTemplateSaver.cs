using Karpik.Engine.Shared.AssetManagement.Base;

namespace Karpik.Engine.Shared.ECS;

public class ComponentsTemplateSaver : JsonSaver<ComponentsTemplateAsset>
{
    public ComponentsTemplateSaver()
    {
        Serializer.Converters.Add(new ComponentArrayConverter());
    }
}