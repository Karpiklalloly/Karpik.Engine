using Karpik.Engine.Shared;

namespace Karpik.Engine.Client;

public class DrawSpriteSystem : IEcsRun, IEcsInject<EcsDefaultWorld>
{
    public class Aspect : EcsAspect
    {
        public EcsPool<SpriteRenderer> sprite = Inc;
        public EcsPool<Position> position = Inc;
        public EcsPool<Rotation> rotation = Inc;
        public EcsPool<Scale> scale = Inc;
    }
    
    private EcsDefaultWorld _world;
    
    public void Run()
    {
        var span = _world.Where(out Aspect a);
        foreach (var e in span)
        {
            var sprite = a.sprite.Get(e);
            var position = a.position.Get(e);
            var rotation = a.rotation.Get(e);
            var scale = a.scale.Get(e);
            Drawer.Sprite(sprite, position, rotation, scale);
        }
    }

    public void Inject(EcsDefaultWorld obj)
    {
        _world = obj;
    }
}