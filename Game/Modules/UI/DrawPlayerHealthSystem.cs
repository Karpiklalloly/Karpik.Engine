using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client;
using Karpik.Engine.Client.VisualElements;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Game.Modules;

public class DrawPlayerHealthSystem : IEcsRun
{
    private class Aspect : EcsAspect
    {
        public EcsPool<Health> health = Inc;
        public EcsTagPool<Player> player = Inc;
    }

    private EcsDefaultWorld _world = Worlds.Instance.World;
    private ProgressBar _bar;
    
    public void Run()
    {
        var entities = _world.Where(out Aspect a);
        if (entities.Count == 0) return;
        
        var health = a.health.Get(entities[0]);
        if (_bar == null)
        {
            _bar = new ProgressBar(new Vector2(300, 20))
            {
                Pivot = Vector2.Zero,
                OffsetPosition = new Vector2(10, 10),
                Font = UI.DefaultFont,
                ForegroundColor = Color.Red,
                BackgroundColor = Color.Black
            };
            UI.Root.Add(_bar);
        }
        
        _bar.Value = health.Value;
        _bar.MaxValue = health.Max.ModifiedValue;
    }
}