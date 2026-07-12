using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

internal class PublishInputSystem : ISystemMainThreadBegin
{
    [DI] private Input _input = null!;

    public void MainThreadBegin()
    {
        _input.PublishPlatformFrame();
    }
}

internal class ConsumeInputSystem : ISystemBegin
{
    [DI] private Input _input = null!;

    public void Begin()
    {
        _input.ConsumeSimulationFrame();
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
