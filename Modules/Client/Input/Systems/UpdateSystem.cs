using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

internal class UpdateSystem : ISystemBegin
{
    [DI] private Input _input = null!;
    
    public void Begin()
    {
        _input.Update();
    }
}

internal class DestroySystem : ISystemDestroy
{
    [DI] private Input _input = null!;

    public void Destroy()
    {
        _input.Destroy();
    }
}