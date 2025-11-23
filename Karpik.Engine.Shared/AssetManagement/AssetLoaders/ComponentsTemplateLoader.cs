namespace Karpik.Engine.Shared;

public class ComponentsTemplateLoader : JsonLoader<ComponentsTemplate>
{
    public ComponentsTemplateLoader()
    {
        Serializer.Converters.Add(new ComponentArrayConverter());
    }
}