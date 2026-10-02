using System.Runtime.Serialization;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Karpik.Engine.Shared.ECS;

[Serializable]
public class ComponentsTemplate
{
    [JsonIgnore]
    public ComponentTemplateBase[] Components;
    [JsonProperty("Components")]
    private IEcsComponentMember[] _components;
    private bool _isMaterialized;

    public ComponentsTemplate()
    {
        Components = [];
        _components = [];
        _isMaterialized = true;
    }

    public ComponentsTemplate(params ComponentTemplateBase[] components)
    {
        Components = components;
        _components = [];
        _isMaterialized = true;
    }

    public ComponentsTemplate(ILogger log, params IEcsComponentMember[] components)
    {
        _components = components;
        Components = Convert(components, log);
        _isMaterialized = true;
    }

    internal static ComponentsTemplate FromRawComponents(IEcsComponentMember[] components)
    {
        if (components.Any(component => component is not IEcsComponent and not IEcsTagComponent))
        {
            throw new InvalidOperationException("Snapshot contains an unsupported ECS component member.");
        }

        return new ComponentsTemplate
        {
            _components = components,
            Components = [],
            _isMaterialized = false
        };
    }

    public async JobHandle ApplyTo(int entityID, EcsWorld world, IServiceResolver container)
    {
        foreach (var template in Components)
        {
            template.ApplyTo(entityID, world);
            await template.OnLoad(container, entityID, world);
        }
    }
    
    [OnSerializing]
    private void OnSerialize(StreamingContext context)
    {
        if (_isMaterialized)
        {
            _components = Components.Select(x => (IEcsComponentMember)x.GetRaw()).ToArray();
        }
    }
    
    [OnDeserialized]
    private void OnDeserialize(StreamingContext context)
    {
        Components = [];
        _isMaterialized = false;
    }

    internal void Materialize(ILogger log)
    {
        if (_isMaterialized)
        {
            return;
        }

        Components = Convert(_components, log);
        _isMaterialized = true;
    }

    private static ComponentTemplateBase[] Convert(IEcsComponentMember[] components, ILogger log)
    {
        var c = components.Select(component => ConvertFrom(component, log)).Where(x => x is not null).ToArray();
        return c;
    }

    private static ComponentTemplateBase? ConvertFrom(IEcsComponentMember x, ILogger log)
    {
        return x switch
        {
            IEcsComponent component => component.ToComponentTemplate(log),
            IEcsTagComponent tagComponent => tagComponent.ToComponentTemplate(log),
            _ => throw new InvalidOperationException($"Unknown component member type: {x.GetType().FullName}"),
        };
    }
}
