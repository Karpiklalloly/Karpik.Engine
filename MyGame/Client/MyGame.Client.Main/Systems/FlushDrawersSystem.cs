using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

[SequentialSystem]
public class FlushDrawersSystem : ISystemRenderPrepare
{
    [DI] private Drawer _drawer;

    public void RenderPrepare()
    {
        _drawer.Draw();
    }
}
