using DCFApixels.DragonECS;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Game.Modules;

public class DealDamageSystem : RunOnRequestSystem<DealDamageRequest, DealDamageSystem.Aspect>
{
    public class Aspect : EcsAspect
    {
        public EcsPool<Health> health = Inc;
    }
    
    protected override void RunOnEvent(ref DealDamageRequest evt, ref Aspect aspect)
    {
        ref var health = ref aspect.health.Get(evt.Target);
        health.Value -= (float)evt.Damage;
    }
}