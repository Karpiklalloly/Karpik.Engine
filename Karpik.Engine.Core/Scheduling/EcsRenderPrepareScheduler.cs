using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class EcsRenderPrepareScheduler : IDisposable
{
    private readonly EcsUpdateScheduler _scheduler = new();

    public EcsUpdateGraph Graph => _scheduler.Graph;

    public void Initialize(
        ReadOnlySpan<ISystemRenderPrepare> systems,
        ReadOnlySpan<EcsUpdateSystemDescriptor> descriptors,
        EcsUpdateSchedulerMode mode = EcsUpdateSchedulerMode.Parallel,
        int workerCount = -1)
    {
        _scheduler.InitializeRenderPrepare(systems, descriptors, mode, workerCount);
    }

    public void RenderPrepare()
    {
        _scheduler.Update();
    }

    public void Dispose()
    {
        _scheduler.Dispose();
    }
}
