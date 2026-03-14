using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Physics.Core;

public class Physics2DBodyCreator : IEcsRun
{
    class Aspect : EcsAspect 
    {
        public EcsPool<Transform2D> Transforms = Inc;
        public EcsPool<CreateBodyRequest> Requests = Inc;
        public EcsPool<PhysicsBodyRef> BodyRefs = Opt;
    }
    
    [DI] private IPhysicsWorld2D _physics = null!;
    [DI] private EcsDefaultWorld _world = null!;
    
    public void Run()
    {
        foreach (var e in _world.Where(out Aspect create))
        {
            ref var request = ref create.Requests.Get(e);
            ref var transform = ref create.Transforms.Get(e);

            var handle = _physics.CreateBody(
                e,
                transform.Position,
                transform.Rotation,
                request.BodyConfig,
                request.ShapeConfig);
            
            create.BodyRefs.TryAddOrGet(e).Handle = handle;
            create.Requests.Del(e);
        }
    }
}