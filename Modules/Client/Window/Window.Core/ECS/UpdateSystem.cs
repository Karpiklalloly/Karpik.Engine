using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

internal class UpdateSystem : IEcsRun
{
    [DI] private IInputSource _inputSource = null!;
    
    public void Run()
    {
        _inputSource.Update();
    }
}