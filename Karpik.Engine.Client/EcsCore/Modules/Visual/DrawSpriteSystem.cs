using Karpik.Engine.Shared;

namespace Karpik.Engine.Client;

public class DrawSpriteSystem : IEcsRunParallel
{
    public class Aspect : EcsAspect
    {
        public EcsReadonlyPool<SpriteRenderer> sprite = Inc;
        public EcsReadonlyPool<Position> position = Inc;
        public EcsReadonlyPool<Rotation> rotation = Inc;
        public EcsReadonlyPool<Scale> scale = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    [DI] private Drawer _drawer;
    
    public void RunParallel()
    {
        var span = _world.Where(out Aspect a);
        foreach (var e in span)
        {
            var sprite = a.sprite.Get(e);
            var position = a.position.Get(e);
            var rotation = a.rotation.Get(e);
            var scale = a.scale.Get(e);
            _drawer.Sprite(sprite, position, rotation, scale);
        }
    }
}