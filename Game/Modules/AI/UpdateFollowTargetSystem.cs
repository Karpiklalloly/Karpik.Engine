using DCFApixels.DragonECS;
using Karpik.Engine.Shared;

namespace Karpik.Game.Modules;

public class UpdateFollowTargetSystem : IEcsRun
{
    private class AspectPlayer : EcsAspect
    {
        public EcsTagPool<FollowPlayer> followPlayer = Inc;
    }
    
    private EcsDefaultWorld _world;
    private EcsPool<FollowTarget> _followTargetPool;

    public UpdateFollowTargetSystem()
    {
        _world = Worlds.Instance.World;
        _followTargetPool = _world.GetPool<FollowTarget>();
    }
    
    public void Run()
    {
        foreach (var e in _world.Where(out AspectPlayer aPlayer))
        {
            ref var followTarget = ref _followTargetPool.TryAddOrGet(e);
            if (followTarget.Target == 0)
            {
                followTarget.Target = Worlds.Instance.MetaWorld.GetPlayer().Player.ID;
            }
        }
    }
}