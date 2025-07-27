using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;

namespace Karpik.Engine.Client;

public class DisplaySystem : IEcsRun, IEcsInject<EcsDefaultWorld>
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
            ref readonly var pos = ref a.position.Get(e);
            ref readonly var health = ref a.health.Get(e);
        }
    }

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}