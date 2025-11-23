namespace Karpik.Engine.Shared;

public class ComponentsTemplateSaver : JsonSaver
{
    public ComponentsTemplateSaver()
    {
        Serializer.Converters.Add(new ComponentArrayConverter());
    }
}