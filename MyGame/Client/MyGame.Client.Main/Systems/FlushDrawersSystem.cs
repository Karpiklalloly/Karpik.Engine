using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

/// <summary>
/// Flushes all accumulated draw actions after 3D rendering is complete.
/// This system must run AFTER PreEndContextSystem (EndMode3D) but BEFORE EndContextSystem.
/// </summary>
public class FlushDrawersSystem : IEcsRun
{
    [DI] private Drawer _drawer;
    
    public void Run()
    {
        _drawer.Draw();
    }
}
