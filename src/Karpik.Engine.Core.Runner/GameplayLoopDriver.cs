using DCFApixels.DragonECS;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Core.Runner;

public sealed class GameplayLoopDriver
{
    private readonly Time _time;
    private readonly EcsPipeline _pipeline;
    private readonly EcsBeginRunner _beginRunner;
    private readonly EcsFixedRunner _fixedRunner;
    private readonly EcsUpdateScheduler _updateScheduler;
    private readonly EcsLateRunner _lateRunner;

    internal GameplayLoopDriver(
        Time time,
        EcsPipeline pipeline,
        EcsBeginRunner beginRunner,
        EcsFixedRunner fixedRunner,
        EcsUpdateScheduler updateScheduler,
        EcsLateRunner lateRunner)
    {
        _time = time;
        _pipeline = pipeline;
        _beginRunner = beginRunner;
        _fixedRunner = fixedRunner;
        _updateScheduler = updateScheduler;
        _lateRunner = lateRunner;
    }

    public void StepBegin()
    {
        _beginRunner.BeginRun();
    }

    public void StepRun()
    {
        _pipeline.Run();
    }

    public void StepFixed(int ticks = 1)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks));
        }

        for (int i = 0; i < ticks; i++)
        {
            _fixedRunner.FixedRun();
        }
    }

    public void StepUpdate(double deltaTime)
    {
        _time.Update(deltaTime);
        _updateScheduler.Update();
    }

    public void StepLate()
    {
        _lateRunner.LateRun();
    }

    public void StepFrame(double deltaTime, int fixedTicks = 1, bool update = true, bool late = true)
    {
        _time.Update(deltaTime);
        StepBegin();
        StepRun();
        StepFixed(fixedTicks);
        if (update)
        {
            _updateScheduler.Update();
        }

        if (late)
        {
            StepLate();
        }
    }
}
