using System.Runtime.Serialization;

namespace Karpik.Engine.Shared;

[Serializable]
public class ComponentsTemplate : Asset
{
    [JsonIgnore]
    public ComponentTemplateBase[] Components;
    [JsonProperty("Components")]
    private IEcsComponentMember[] _components;

    public ComponentsTemplate()
    {
        Components = [];
    }

    public ComponentsTemplate(params ComponentTemplateBase[] components)
    {
        Components = components;
    }

    public ComponentsTemplate(params IEcsComponentMember[] components)
    {
        Components = Convert(components);
    }

    public void ApplyTo(int entityID, EcsWorld world)
    {
        foreach (var template in Components)
        {
            template.ApplyTo(entityID, world);
        }
    }
    
    [OnSerializing]
    private void OnSerialize(StreamingContext context)
    {
        _components = Components.Select(x => (IEcsComponentMember)x.GetRaw()).ToArray();
    }
    
    [OnDeserialized]
    private void OnDeserialize(StreamingContext context)
    {
        Components = Convert(_components);
    }

    private ComponentTemplateBase[] Convert(params IEcsComponentMember[] components)
    {
        var c = components.Select(ConvertFrom).Where(x => x is not null).ToArray();
        return c;
    }

    private ComponentTemplateBase ConvertFrom(IEcsComponentMember x)
    {
        return x switch
        {
            IEcsComponent component => component.ToComponentTemplate(),
            IEcsTagComponent tagComponent => tagComponent.ToComponentTemplate(),
            _ => null
        };
    }

    protected override void OnUnload()
    {
        Components = null;
        _components = null;
    }
}