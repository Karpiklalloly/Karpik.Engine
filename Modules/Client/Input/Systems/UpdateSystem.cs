using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

internal class PublishInputSystem(Input input) : ISystemMainThreadBegin
{
    public void MainThreadBegin()
    {
        input.PublishPlatformFrame();
    }
}

internal class ConsumeInputSystem(Input input) : ISystemBegin
{
    public void Begin()
    {
        input.ConsumeSimulationFrame();
    }
}

internal class DestroySystem(Input input) : ISystemDestroy
{
    public void Destroy()
    {
        input.Destroy();
    }
}
