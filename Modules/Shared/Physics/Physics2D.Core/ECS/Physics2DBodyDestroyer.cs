using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.Shared.Physics.Core;

public class Physics2DBodyDestroyer : ISystemLateUpdate
{
    class Aspect : EcsAspect 
    {
        public EcsPool<PhysicsBodyRef> BodyRefs = Inc;
        public EcsPool<DestroyBodyRequest> Requests = Inc;
    }
    
    [DI] private IPhysicsWorld2D _physics = null!;
    [DI] private DefaultWorld _world = null!;
    
    public void LateUpdate()
    {
        foreach (var e in _world.Where(out Aspect destroy))
        {
            var handle = destroy.BodyRefs.Get(e).Handle;
            _physics.DestroyBody(handle);
            
            destroy.BodyRefs.Del(e);
            destroy.Requests.Del(e);
        }
    }
}