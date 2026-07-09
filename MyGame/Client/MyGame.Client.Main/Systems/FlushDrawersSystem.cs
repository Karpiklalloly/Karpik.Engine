using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class FlushDrawersSystem : ISystemRenderPrepare
{
    [DI] private Drawer _drawer;

    public void RenderPrepare()
    {
        _drawer.Draw();
    }
}
