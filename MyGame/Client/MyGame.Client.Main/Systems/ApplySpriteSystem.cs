using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class ApplySpriteSystem : IEcsRun
{
    class Aspect : EcsAspect
    {
        public EcsPool<SpriteData> data = Inc;
        public EcsPool<SpriteRenderer> renderer = Exc;
        public EcsPool<IgnoreSpriteData> ignore = Exc;
    }

    [DI] private EcsDefaultWorld _world = null!;
    [DI] private IServiceContainer _serviceContainer = null!;
    
    public void Run()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            var data = a.data.Get(e);
            a.ignore.Add(e);
            a.data.Del(e);
            Job.Run(async () =>
            {
                ref var renderer = ref a.renderer.Add(e);

                renderer.TexturePath = data.TexturePath;
                renderer.Color = data.Color;
                var r2 = await renderer.OnLoad(renderer, _serviceContainer);
                r2.Width = data.Size.X;
                r2.Height = data.Size.Y;
                a.renderer.Get(e) = r2;
            });
        }
    }
}