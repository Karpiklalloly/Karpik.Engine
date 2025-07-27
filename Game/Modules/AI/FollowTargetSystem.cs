using DCFApixels.DragonECS;
using Karpik.Engine.Shared;

namespace Karpik.Game.Modules;

public class FollowTargetSystem : IEcsRun, IEcsInject<EcsDefaultWorld>
{
    private class Aspect : EcsAspect
    {
        public EcsPool<Position> position = Inc;
        public EcsPool<FollowTarget> followTarget = Inc;
        public EcsPool<Speed> speed = Inc;
    }
    
    private EcsDefaultWorld _world;

    public void Run()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            ref var transform = ref a.position.Get(e);
            ref var followTarget = ref a.followTarget.Get(e);
            ref var speed = ref a.speed.Get(e);
            
            ref var targetTransform = ref a.position.Get(followTarget.Target);

            var entity = _world.GetEntityLong(e);
            // var direction = (targetTransform.Position - transform.Position);
            // direction.Normalized();
            // entity.MoveBySpeed(direction);
        }
    }

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}