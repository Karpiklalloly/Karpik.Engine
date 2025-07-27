using Karpik.Engine.Shared;

namespace Karpik.Engine.Server.DEMO;

public class PlayerInputSystem : IEcsRun, IEcsInject<EcsDefaultWorld>
{
    class Aspect : EcsAspect
    {
        //public EcsPool<PlayerInputCommand> input = Inc;
        public EcsPool<Position> position = Inc;
    }
    
    private EcsDefaultWorld _world;
    
    public void Run()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            var mask = _world.Where(EcsStaticMask.Inc<Position>().Build());
            foreach (var ff in mask)
            {
                
            }
            // ref readonly var input = ref a.input.Get(e);
            // ref var pos = ref a.position.Get(e);
            // pos.X += input.HorizontalInput;
            // a.input.Del(e);
        }
    }

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}