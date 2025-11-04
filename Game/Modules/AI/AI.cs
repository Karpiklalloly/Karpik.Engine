using DCFApixels.DragonECS;
using Karpik.Engine.Shared;

namespace Karpik.Game.Modules;

public static class AI
{
    public static void Follow(this entlong entity, int target)
    {
        entity.Add<FollowTarget>() = new FollowTarget() 
        {
            Target = target
        };
    }

    public static void FollowPlayer(this entlong entity, EcsMetaWorld metaWorld, EcsDefaultWorld world)
    {
        ref var player = ref metaWorld.GetPlayer(world);
        
        entity.Add<FollowTarget>() = new FollowTarget() 
        {
            Target = player.Player.ID
        };
    }

    public static ref PlayerRef GetPlayer(this EcsMetaWorld ecsMetaWorld, EcsDefaultWorld world)
    {
        ref var player = ref ecsMetaWorld.Get<PlayerRef>();
        if (player.Player.IsNull)
        {
            var players = world.Where(EcsStaticMask.Inc<Player>().Build());
            if (players.Count > 0)
            {
                player = new PlayerRef()
                {
                    Player = players.Longs[0]
                };
            }
            
        }

        return ref player;
    }
}