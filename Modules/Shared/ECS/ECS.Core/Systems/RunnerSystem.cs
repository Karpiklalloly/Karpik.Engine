namespace Karpik.Engine.Shared.ECS;

internal class RunnerSystem : IEcsInit, IEcsPipelineMember, IEcsRun, IEcsDestroy
{
    public EcsPipeline Pipeline { get; set; }
    
    private EcsPausableRunner _pausableRunner = null!;
    private EcsRunParallelRunner _parallelRunner = null!;
    private PausableLateRunner _pausableLateRunner = null!;
    private EcsRunLateRunner _lateRunner = null!;
    
    public void Init()
    {
        _pausableRunner = Pipeline.GetRunner<EcsPausableRunner>();
        _parallelRunner = Pipeline.GetRunner<EcsRunParallelRunner>();
        _pausableLateRunner = Pipeline.GetRunner<PausableLateRunner>();
        _lateRunner = Pipeline.GetRunner<EcsRunLateRunner>();
        _parallelRunner.Init();
    }

    public void Run()
    {
        _pausableRunner.PausableRun();
        _parallelRunner.RunParallel();
        _pausableLateRunner.PausableLateRun();
        _lateRunner.RunLate();
        BaseSystem.RunBuffers();
    }

    public void Destroy()
    {
        _parallelRunner.Destroy();
    }
}