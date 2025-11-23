using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Server.DEMO;

public class OnJumpSystem : IEcsRunOnEvent<JumpCommand>
{
    [DI] private EcsDefaultWorld _world;
    
    public void RunOnEvent(ref JumpCommand command)
    {
        var entity = _world.FindByNetworkId(command.Target);
        if (!entity.IsAlive) return;
        var positionPool = _world.GetPool<Position>();
        if (!positionPool.Has(entity.ID)) return;
        ref var position = ref positionPool.Get(entity.ID);
        position.Y += 1;
    }   
}