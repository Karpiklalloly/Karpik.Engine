using DCFApixels.DragonECS.RunnersCore;

namespace Karpik.Engine.Shared.DragonECS;

public interface IBeginRunSystem : IEcsProcess
{
    public void BeginRun();
}
public class EcsBeginRunner : EcsRunner<IBeginRunSystem>, IBeginRunSystem
{
    public void BeginRun()
    {
        foreach (var run in Process)
        {
            run.BeginRun();
        }
    }
}

public interface IEcsFixedRun : IEcsProcess
{
    public void FixedRun();
}
public class EcsFixedRunner : EcsRunner<IEcsFixedRun>, IEcsFixedRun
{
    public void FixedRun()
    {
        foreach (var run in Process)
        {
            run.FixedRun();
        }
    }
}

public interface IUpdateSystem : IEcsProcess
{
    public void Update();
}
public class EcsUpdateRunner : EcsRunner<IUpdateSystem>, IUpdateSystem
{
    public void Update()
    {
        foreach (var run in Process)
        {
            run.Update();
        }
    }
}

public interface ILateRunSystem : IEcsProcess
{
    public void LateRun();
}
public class EcsLateRunner : EcsRunner<ILateRunSystem>, ILateRunSystem
{
    public void LateRun()
    {
        foreach (var run in Process)
        {
            run.LateRun();
        }
    }
}

public interface IRenderSystem : IEcsProcess
{
    public void Render();
}
public class EcsRenderRunner : EcsRunner<IRenderSystem>, IRenderSystem
{
    public void Render()
    {
        foreach (var run in Process)
        {
            run.Render();
        }
    }
}