using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.ECS;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class DisplaySystem : IEcsRun
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<Position> position = Inc;
    }
    
    [DI] private EcsDefaultWorld _world;
    // [DI] private IRenderer _renderer;
    [DI] private Drawer _drawer;
    [DI] private GraphicsContext _context = null!;
    
    public void Run()
    {
        GraphicsContext.Buffer.Add(new DrawRectCmd()
        {
            Color = Color.Blue,
            Rectangle = new RectangleF(-1, -1, 100, 100)
        });
        foreach (var e in _world.Where(out Aspect a))
        {
            ref readonly var pos = ref a.position.Get(e);
            // _renderer.DrawSphere(new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z),
            //     1,
            //     Color.Red);
        }
    }
}