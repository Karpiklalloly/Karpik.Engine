using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Core;

public class UpdateSystem(IInputSource inputSource) : ISystemMainThreadBegin
{
    public void MainThreadBegin()
    {
        inputSource.Update();
    }
}
