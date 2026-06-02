using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class DrawSpriteSystem : ISystemRender
{
    public class Aspect : EcsAspect
    {
        public EcsReadonlyPool<SpriteRenderer> sprite = Inc;
        public EcsReadonlyPool<Transform2D> position = Inc;
    }
    
    [DI] private DefaultWorld _world;
    [DI] private Drawer _drawer;
    
    public void Render()
    {
        var span = _world.Where(out Aspect a);
        foreach (var e in span)
        {
            var sprite = a.sprite.Get(e);
            var position = a.position.Get(e);
            
            _drawer.Sprite(sprite, position);
        }
    }
}
