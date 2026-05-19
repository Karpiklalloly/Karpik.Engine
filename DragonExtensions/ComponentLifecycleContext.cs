using Karpik.Engine.Core;

namespace DragonExtensions;

public struct ComponentLifecycleContext
{
    public readonly IServiceContainer Services;
    public readonly EcsWorld World;
    public readonly int EntityId;

    public ComponentLifecycleContext(IServiceContainer services, EcsWorld world, int entityId)
    {
        Services = services;
        World = world;
        EntityId = entityId;
    }
}