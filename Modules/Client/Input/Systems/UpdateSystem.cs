using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

internal class UpdateSystem : IEcsRun
{
    [DI] private Input _input;
    
    public void Run()
    {
        _input.Update();
    }
}