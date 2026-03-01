using System.Diagnostics;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Shared.ECS;

internal class RunnerSystem : IEcsInit, IEcsPipelineMember, IEcsRun, IEcsDestroy
{
    public EcsPipeline Pipeline { get; set; }
    
    private EcsPausableRunner _pausableRunner = null!;
    private EcsRunParallelRunner _parallelRunner = null!;
    private PausableLateRunner _pausableLateRunner = null!;
    private EcsRunLateRunner _lateRunner = null!;
    private EcsFixedRunner _fixedRunner = null!;

    [DI] private Application _application = null!;
    private Action _fixedRun = null!;
    
    private Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _nextTickTime = 0;
    
    public void Init()
    {
        _pausableRunner = Pipeline.GetRunner<EcsPausableRunner>();
        _parallelRunner = Pipeline.GetRunner<EcsRunParallelRunner>();
        _pausableLateRunner = Pipeline.GetRunner<PausableLateRunner>();
        _lateRunner = Pipeline.GetRunner<EcsRunLateRunner>();
        _fixedRunner = Pipeline.GetRunner<EcsFixedRunner>();
        _parallelRunner.Init();

        if (_application.ApplicationSide == Side.Server)
        {
            _fixedRun = () => _fixedRunner.FixedRun();
        }
        else if (_application.ApplicationSide == Side.Client)
        {
            _nextTickTime = _stopwatch.Elapsed.TotalSeconds;
            _fixedRun = () =>
            {
                double currentTime = _stopwatch.Elapsed.TotalSeconds;
                int loops = 0;
            
                while (currentTime >= _nextTickTime && loops < 5)
                {
                    _fixedRunner.FixedRun();
                    _nextTickTime += Application.TICK_DT;
                    loops++;
                }
            
                if (loops >= 5)
                {
                    Console.WriteLine($"Server overloading! Skipping ticks. Lag: {currentTime - _nextTickTime:F4}s");
                    _nextTickTime = currentTime + Application.TICK_DT;
                }
            };
        }
    }

    public void Run()
    {
        _fixedRun.Invoke();
        _pausableRunner.PausableRun();
        _parallelRunner.RunParallel();
        _pausableLateRunner.PausableLateRun();
        _lateRunner.RunLate();
    }

    public void Destroy()
    {
        _parallelRunner.Destroy();
    }
}