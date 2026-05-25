using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.Shared.Physics.Core;

public class Physics2DBodyCreator : ISystemBegin
{
    class Aspect : EcsAspect 
    {
        public EcsPool<Transform2D> Transforms = Inc;
        public EcsPool<CreateBodyRequest> Requests = Inc;
        public EcsPool<PhysicsBodyDefinition> Definitions = Opt;
        public EcsPool<PhysicsBodyRef> BodyRefs = Exc;
    }
    
    [DI] private IPhysicsWorld2D _physics = null!;
    [DI] private DefaultWorld _world = null!;
    
    public void Begin()
    {
        foreach (var e in _world.Where(out Aspect create))
        {
            ref var request = ref create.Requests.Get(e);
            ref var transform = ref create.Transforms.Get(e);

            ref var definition = ref create.Definitions.TryAddOrGet(e);
            definition.BodyConfig = request.BodyConfig;
            definition.ShapeConfig = request.ShapeConfig;

            var handle = _physics.CreateBody(
                e,
                transform.Position,
                transform.Rotation,
                request.BodyConfig,
                request.ShapeConfig);
            
            create.BodyRefs.Add(e).Handle = handle;
            create.Requests.Del(e);
        }
    }
}
