using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

internal class UpdateSystem : IEcsRun
{
    [DI] private Input _input = null!;
    
    public void Run()
    {
        _input.Update();
    }
}