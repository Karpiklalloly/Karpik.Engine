using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

internal class UpdateSystem : ISystemMainThreadBegin
{
    [DI] private IInputSource _inputSource = null!;
    
    public void MainThreadBegin()
    {
        _inputSource.Update();
    }
}
