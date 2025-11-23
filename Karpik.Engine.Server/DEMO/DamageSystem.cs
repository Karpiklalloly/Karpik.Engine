using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;

namespace Karpik.Engine.Server.DEMO;

public class DamageSystem : IEcsRunParallel
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<Position> position = Inc;
        public EcsPool<Health> health = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    
    public void RunParallel()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            ref readonly var pos = ref a.position.Get(e);
            if (pos.X > 10)
            {
                ref var health = ref a.health.Get(e);
                if (health.Value > 0)
                {
                    health.Value -= 1;
                    if (health.Value <= 0)
                    {
                        Console.WriteLine("Death");
                    }
                }
            }
        }
    }
}