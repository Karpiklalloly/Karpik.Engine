using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class DisplaySystem : IEcsRunParallel
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<Position> position = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    [DI] private IRenderer _renderer;
    
    public void RunParallel()
    {
        foreach (var e in _world.Where(out Aspect a))
        {
            ref readonly var pos = ref a.position.Get(e);
            _renderer.DrawSphere(new Vector3((float)pos.Value[0], (float)pos.Value[1], (float)pos.Value[2]),
                1,
                Color.Red);
        }
    }
}