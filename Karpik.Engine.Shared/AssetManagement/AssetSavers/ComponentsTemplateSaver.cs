using Karpik.Engine.Shared.AssetManagement;

namespace Karpik.Engine.Shared;

public class ComponentsTemplateSaver : JsonSaver<ComponentsTemplateAsset>
{
    public ComponentsTemplateSaver()
    {
        Serializer.Converters.Add(new ComponentArrayConverter());
    }
}