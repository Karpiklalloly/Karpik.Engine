using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;

namespace Karpik.Engine.Server.DEMO;

public class DamageSystem : IEcsRun, IEcsInject<EcsDefaultWorld>
{
    class Aspect : EcsAspect
    {
        public EcsPool<Position> position = Inc;
        public EcsPool<Health> health = Inc;
    }
    
    private EcsDefaultWorld _world;
    
    public void Run()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            ref var pos = ref a.position.Get(e);
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

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}