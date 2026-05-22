using DragonExtensions;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.ECS;

public class DefaultWorld(EcsDefaultWorld world, IServiceContainer container) : World(world, container)
{
    
}

public class EventWorld(EcsEventWorld world, IServiceContainer container) : World(world, container)
{
    
}

public class MetaWorld(EcsMetaWorld world, IServiceContainer container) : World(world, container)
{
    
}