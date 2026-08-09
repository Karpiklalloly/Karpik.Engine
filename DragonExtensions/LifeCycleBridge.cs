using Karpik.Engine.Core;

namespace DragonExtensions;

public class InitSystem(ISystemInit system) : IEcsInit
{
    public void Init()
    {
        system.Init();
    }
}

public class BeginSystem(ISystemBegin system) : IBeginRunSystem
{
    public void BeginRun()
    {
        system.Begin();
    }
}

public class MainThreadBeginSystem(ISystemMainThreadBegin system) : IMainThreadBeginRunSystem
{
    public void MainThreadBegin()
    {
        system.MainThreadBegin();
    }
}

public class MainThreadFrameBeginSystem(ISystemMainThreadFrameBegin system) : IMainThreadFrameBeginRunSystem
{
    public void MainThreadFrameBegin()
    {
        system.MainThreadFrameBegin();
    }
}

public class FixedUpdateSystem(ISystemFixedUpdate system) : IEcsFixedRun
{
    public void FixedRun()
    {
        system.FixedUpdate();
    }
}

public class UpdateSystem(ISystemUpdate system) : IUpdateSystem
{
    public ISystemUpdate System => system;

    public void Update()
    {
        system.Update();
    }
}

public class LateSystem(ISystemLateUpdate system) : ILateRunSystem
{
    public void LateRun()
    {
        system.LateUpdate();
    }
}

public class RenderSystem(ISystemRender system) : IRenderSystem
{
    public void Render()
    {
        system.Render();
    }
}

public class RenderPrepareSystem(ISystemRenderPrepare system) : IRenderPrepareSystem
{
    public ISystemRenderPrepare System => system;

    public void RenderPrepare()
    {
        system.RenderPrepare();
    }
}

public class DestroySystem(ISystemDestroy system) : IEcsDestroy
{
    public void Destroy()
    {
        system.Destroy();
    }
}
