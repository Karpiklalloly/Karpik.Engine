using System.Numerics;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class DisplaySystem : IEcsRun, IEcsInject<EcsDefaultWorld>
{
    class Aspect : EcsAspect
    {
        public EcsPool<Position> position = Inc;
    }
    
    private EcsDefaultWorld _world;
    
    public void Run()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            ref readonly var pos = ref a.position.Get(e);
            Raylib.DrawSphere(new Vector3((float)pos.Value[0], (float)pos.Value[1], (float)pos.Value[2]),
                1,
                Color.Red);
        }
    }

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}