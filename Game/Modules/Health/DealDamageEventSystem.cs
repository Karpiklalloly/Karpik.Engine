using DCFApixels.DragonECS;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Game.Modules;

public class DealDamageEventSystem : IEcsRunOnEvent<DealDamageEvent>, IEcsInject<EcsDefaultWorld>
{
    private EcsDefaultWorld _world;
    
    public void RunOnEvent(ref DealDamageEvent evt)
    {
        ref var request = ref _world.GetPool<DealDamageRequest>().TryAddOrGet(evt.Target);
        request.Target = evt.Target;
        request.Damage += evt.Damage;
    }

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}