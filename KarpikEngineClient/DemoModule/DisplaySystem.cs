using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class DisplaySystem : IEcsRun, IEcsInject<EcsDefaultWorld>
{
    class Aspect : EcsAspect
    {
        public EcsPool<Position> position = Inc;
        //public EcsPool<Health> health = Inc;
    }
    
    private EcsDefaultWorld _world;
    
    public void Run()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            ref readonly var pos = ref a.position.Get(e);
            Raylib.DrawCircle((int)pos.X, (int)pos.Y, 10, Color.Red);
            //ref readonly var health = ref a.health.Get(e);
        }
    }

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}