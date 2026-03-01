using DCFApixels.DragonECS.RunnersCore;

namespace Karpik.Engine.Shared.DragonECS;

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