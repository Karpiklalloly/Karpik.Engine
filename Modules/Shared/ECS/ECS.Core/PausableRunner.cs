using DCFApixels.DragonECS.RunnersCore;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.ECS;

public interface IEcsPausableRun : IEcsProcess
{
    public void PausableRun();
}

public class EcsPausableRunner : EcsRunner<IEcsPausableRun>, IEcsPausableRun
{
    public void PausableRun()
    {
        if (Time.IsPaused) return;
        foreach (var process in Process)
        {
            process.PausableRun();
        }
    }
}

public interface IEcsPausableLateRun : IEcsProcess
{
    public void PausableLateRun();
}

public sealed class PausableLateRunner : EcsRunner<IEcsPausableLateRun>, IEcsPausableLateRun
{
    public void PausableLateRun()
    {
        if (Time.IsPaused) return;
            
        foreach (var process in Process)
        {
            process.PausableLateRun();
        }
    }
}