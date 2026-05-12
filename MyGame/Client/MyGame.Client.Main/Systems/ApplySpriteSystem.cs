using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class ApplySpriteSystem : ISystemUpdate
{
    class Aspect : EcsAspect
    {
        public EcsPool<SpriteData> data = Inc;
        public EcsPool<SpriteRenderer> renderer = Exc;
        public EcsPool<IgnoreSpriteData> ignore = Exc;
    }

    [DI] private EcsDefaultWorld _world = null!;
    [DI] private IServiceContainer _serviceContainer = null!;
    [DI] private MainThreadScheduler _mainThreadScheduler = null!;
    
    public void Update()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            int entity = e;
            SpriteData data = a.data.Get(entity);
            a.ignore.Add(e);
            a.data.Del(e);
            
            SpriteRenderer renderer = default;
            renderer.TexturePath = data.TexturePath;
            renderer.Color = data.Color;
            renderer = renderer.OnLoad(renderer, _serviceContainer).GetAwaiter().GetResult();
            renderer.Width = data.Size.X;
            renderer.Height = data.Size.Y;

            ref SpriteRenderer storedRenderer = ref a.renderer.Add(entity);
            storedRenderer = renderer;
        }
    }
}
