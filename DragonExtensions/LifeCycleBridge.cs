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

public class FixedUpdateSystem(ISystemFixedUpdate system) : IEcsFixedRun
{
    public void FixedRun()
    {
        system.FixedUpdate();
    }
}

public class UpdateSystem(ISystemUpdate system) : IUpdateSystem
{
    public void Update()
    {
        system.Run();
    }
}

public class LateSystem(ISystemLate system) : ILateRunSystem
{
    public void LateRun()
    {
        system.LateRun();
    }
}

public class RenderSystem(ISystemRender system) : IRenderSystem
{
    public void Render()
    {
        system.Render();
    }
}

public class DestroySystem(ISystemDestroy system) : IEcsDestroy
{
    public void Destroy()
    {
        system.Destroy();
    }
}