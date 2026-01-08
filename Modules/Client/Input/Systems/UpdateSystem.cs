using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Systems;

internal class UpdateSystem : IEcsRun
{
    [DI] private Input _input;
    
    public void Run()
    {
        _input.Update();
    }
}