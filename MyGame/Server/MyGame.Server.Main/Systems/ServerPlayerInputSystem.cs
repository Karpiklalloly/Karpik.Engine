using System.Numerics;
using DragonExtensions;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

internal class InputSystem : IEcsRunOnEvent<PlatformerInputCommand>
{
    class NetworkIdAspect : EcsAspect
    {
        public EcsReadonlyPool<NetworkId> netId = Inc;
    }

    [DI] private EcsDefaultWorld _world = null!;
    [DI] private IPhysicsWorld2D _physicsWorld2D = null!;
    [DI] private Time _time = null!;
    
    public void RunOnEvent(ref PlatformerInputCommand evt)
    {
        var entity = FindByNetworkId(evt.Target, _world);
        if (!entity.IsAlive) return;

        _world.GetPool<WannaMove>().TryAddOrGet(entity.ID) = new WannaMove()
        {
            MoveX = evt.MoveX,
            Jump = evt.Jump
        };
    }
    
    protected entlong FindByNetworkId(int networkId, EcsWorld world)
    {
        var span = world.Where(out NetworkIdAspect aspect);
        foreach (var e in span)
        {
            var id = aspect.netId.Get(e);
            if (id.Id == networkId)
            {
                return world.GetEntityLong(e);
            }
        }
        return entlong.NULL;
    }
}
