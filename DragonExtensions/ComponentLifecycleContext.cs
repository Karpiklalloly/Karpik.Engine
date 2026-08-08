using Karpik.Engine.Core;

namespace DragonExtensions;

public struct ComponentLifecycleContext
{
    public readonly IServiceResolver Services;
    public readonly EcsWorld World;
    public readonly int EntityId;

    public ComponentLifecycleContext(IServiceResolver services, EcsWorld world, int entityId)
    {
        Services = services;
        World = world;
        EntityId = entityId;
    }
}