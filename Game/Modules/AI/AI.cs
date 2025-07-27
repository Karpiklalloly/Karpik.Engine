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

    public static void FollowPlayer(this entlong entity)
    {
        ref var player = ref Worlds.Instance.MetaWorld.GetPlayer();
        
        entity.Add<FollowTarget>() = new FollowTarget() 
        {
            Target = player.Player.ID
        };
    }

    public static ref PlayerRef GetPlayer(this MetaWorld metaWorld)
    {
        ref var player = ref Worlds.Instance.MetaWorld.Get<PlayerRef>();
        if (player.Player.IsNull)
        {
            var players = Worlds.Instance.World.Where(EcsStaticMask.Inc<Player>().Build());
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