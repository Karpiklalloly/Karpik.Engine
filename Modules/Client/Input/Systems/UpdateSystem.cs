using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

public class PublishInputSystem(Input input) : ISystemMainThreadBegin
{
    public void MainThreadBegin()
    {
        input.PublishPlatformFrame();
    }
}

public class ConsumeInputSystem(Input input) : ISystemBegin
{
    public void Begin()
    {
        input.ConsumeSimulationFrame();
    }
}

public class DestroySystem(Input input) : ISystemDestroy
{
    public void Destroy()
    {
        input.Destroy();
    }
}
