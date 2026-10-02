using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.Shared.Physics.Core;

public class Physics2DBodyDestroyer(IPhysicsWorld2D physics, DefaultWorld world) : ISystemLateUpdate
{
    class Aspect : EcsAspect 
    {
        public EcsPool<PhysicsBodyRef> BodyRefs = Inc;
        public EcsPool<DestroyBodyRequest> Requests = Inc;
    }

    public void LateUpdate()
    {
        foreach (var e in world.Where(out Aspect destroy))
        {
            var handle = destroy.BodyRefs.Get(e).Handle;
            physics.DestroyBody(handle);
            
            destroy.BodyRefs.Del(e);
            destroy.Requests.Del(e);
        }
    }
}