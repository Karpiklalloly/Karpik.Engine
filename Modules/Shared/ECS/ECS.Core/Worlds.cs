using System.Composition;
using DragonExtensions;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.ECS;

[Export(typeof(World))]
[Export(typeof(DefaultWorld))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class DefaultWorld(EcsDefaultWorld world, IServiceResolver resolver) : World(world, resolver)
{
    
}

[Export(typeof(World))]
[Export(typeof(EventWorld))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class EventWorld(EcsEventWorld world, IServiceResolver resolver) : World(world, resolver)
{
    
}

[Export(typeof(World))]
[Export(typeof(MetaWorld))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class MetaWorld(EcsMetaWorld world, IServiceResolver resolver) : World(world, resolver)
{
    
}