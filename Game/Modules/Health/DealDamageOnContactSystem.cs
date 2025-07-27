using DCFApixels.DragonECS;
using Karpik.Engine.Server;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Game.Modules;

public class DealDamageOnContactSystem : IEcsFixedRunOnEvent<CollisionsEvent>, IEcsInject<EcsEventWorld>, IEcsInject<EcsDefaultWorld>
{
    private EcsDefaultWorld _world;
    private EcsEventWorld _eventWorld;
    
    private void Process(entlong source, entlong target)
    {
        if (!source.Has<DealDamageOnContact>()) return;
        if (!source.Has<Damage>()) return;
        if (!target.Has<Health>()) return;
        
        var damage = source.Get<Damage>().ModifiedValue;
        _eventWorld.SendEvent(new DealDamageEvent()
        {
            Target = target.ID,
            Damage = damage,
            Source = source.ID,
        });
    }

    public void RunOnEvent(ref CollisionsEvent evt)
    {
        var source = _world.GetEntityLong(evt.Source);
        var target = _world.GetEntityLong(evt.Target);
        Process(source, target);
        Process(target, source);
    }

    public void Inject(EcsEventWorld obj)
    {
        _eventWorld = obj;
    }

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}