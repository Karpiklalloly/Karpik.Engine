using DCFApixels.DragonECS;
using Karpik.Engine.Shared;

namespace Karpik.Game.Modules;

public class UpdateFollowTargetSystem : IEcsRun, IEcsInit
{
    private class AspectPlayer : EcsAspect
    {
        public EcsTagPool<FollowPlayer> followPlayer = Inc;
        public EcsPool<FollowTarget> followTarget = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    [DI] private EcsMetaWorld _metaWorld;
    private EcsPool<FollowTarget> _followTargetPool;
    
    public void Init()
    {
        _followTargetPool = _world.GetPool<FollowTarget>();
    }
    
    public void Run()
    {
        foreach (var e in _world.Where(out AspectPlayer a))
        {
            ref var followTarget = ref _followTargetPool.TryAddOrGet(e);
            if (followTarget.Target == 0)
            {
                followTarget.Target = _metaWorld.GetPlayer(_world).Player.ID;
            }
        }
    }
}