using DCFApixels.DragonECS.RunnersCore;

namespace Karpik.Engine.Shared.ECS;

public interface IEcsRunLate : IEcsProcess
{
    public void RunLate();
}

public class EcsRunLateRunner : EcsRunner<IEcsRunLate>, IEcsRunLate
{
    public void RunLate()
    {
        foreach (var process in Process)
        {
            process.RunLate();
        }
    }
}