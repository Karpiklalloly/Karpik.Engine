using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

internal class UpdateSystem : ISystemBegin
{
    [DI] private IInputSource _inputSource = null!;
    
    public void Begin()
    {
        _inputSource.Update();
    }
}