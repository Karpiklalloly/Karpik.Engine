using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

[SequentialSystem]
public class ApplySpriteSystem : ISystemInit, ISystemUpdate
{
    class Aspect : EcsAspect
    {
        public EcsPool<SpriteData> data = Inc;
        public EcsPool<SpriteRenderer> renderer = Exc;
        public EcsPool<IgnoreSpriteData> ignore = Exc;
    }

    class RuntimeSpriteAspect : EcsAspect
    {
        public EcsPool<SpriteRenderer> Renderer = Inc;
    }

    class IgnoredSpriteDataAspect : EcsAspect
    {
        public EcsPool<IgnoreSpriteData> Ignore = Inc;
    }

    [DI] private DefaultWorld _world = null!;
    [DI] private IServiceContainer _serviceContainer = null!;
    [DI] private MainThreadScheduler _mainThreadScheduler = null!;

    private readonly List<int> _runtimeSpriteEntities = [];
    private readonly List<int> _ignoredSpriteDataEntities = [];

    public void Init()
    {
        _runtimeSpriteEntities.Clear();
        foreach (var entity in _world.Where(out RuntimeSpriteAspect rendererAspect))
        {
            _runtimeSpriteEntities.Add(entity);
        }

        foreach (var entity in _runtimeSpriteEntities)
        {
            _world.DelEnabled(entity, _world.Base.GetPool<SpriteRenderer>());
        }

        _ignoredSpriteDataEntities.Clear();
        foreach (var entity in _world.Where(out IgnoredSpriteDataAspect ignoreAspect))
        {
            _ignoredSpriteDataEntities.Add(entity);
        }

        foreach (var entity in _ignoredSpriteDataEntities)
        {
            _world.Del<IgnoreSpriteData>(entity);
        }
    }
    
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
            renderer.Width = data.Size.X;
            renderer.Height = data.Size.Y;
            _world.AddEnabled(entity, renderer ,a.renderer);
        }
    }
}
