using System.Runtime.Serialization;
using Karpik.Engine.Shared.AssetManagement.Base;
using Karpik.Jobs;
using Newtonsoft.Json;

namespace Karpik.Engine.Shared.ECS;

[Serializable]
public class ComponentsTemplate
{
    [JsonIgnore]
    public ComponentTemplateBase[] Components;
    [JsonProperty("Components")]
    private IEcsComponentMember[] _components;

    private IAssetsManager _manager;

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

    public async JobHandle ApplyTo(int entityID, EcsWorld world)
    {
        foreach (var template in Components)
        {
            template.ApplyTo(entityID, world);
            await template.OnLoad(_manager, entityID, world);
        }
    }

    public void OnLoad(IAssetsManager manager)
    {
        _manager = manager;
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
}