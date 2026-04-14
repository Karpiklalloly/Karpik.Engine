using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

internal class UpdateSystem : IEcsRun, IEcsDestroy
{
    [DI] private Input _input = null!;
    
    public void Run()
    {
        _input.Update();
    }

    public void Destroy()
    {
        _input.Destroy();
    }
}