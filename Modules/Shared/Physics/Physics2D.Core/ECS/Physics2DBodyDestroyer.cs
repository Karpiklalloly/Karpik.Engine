using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Physics.Core;

public class Physics2DBodyDestroyer : IEcsRun
{
    class DestroyAspect : EcsAspect 
    {
        public EcsPool<PhysicsBodyRef> BodyRefs = Inc;
        public EcsPool<DestroyBodyRequest> Requests = Inc;
    }
    
    [DI] private IPhysicsWorld2D _physics = null!;
    [DI] private EcsDefaultWorld _world = null!;
    
    public void Run()
    {
        foreach (var e in _world.Where(out DestroyAspect destroy))
        {
            var handle = destroy.BodyRefs.Get(e).Handle;
            _physics.DestroyBody(handle);
            
            destroy.BodyRefs.Del(e);
            destroy.Requests.Del(e);
        }
    }
}