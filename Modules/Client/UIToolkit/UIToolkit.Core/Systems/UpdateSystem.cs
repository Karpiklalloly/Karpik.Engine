using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.UIToolkit.Systems;

public class UpdateSystem : IEcsRun
{
    [DI] private UIManager _manager = null!;
    
    public void Run()
    {
        _manager.Update();
        _manager.Render();
    }
}