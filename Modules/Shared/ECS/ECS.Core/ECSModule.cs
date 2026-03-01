using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Shared.ECS;

internal class ECSModule : IEcsModule
{
    public const string RunnerLayer = "CustomRunersLayer";
    
    public void Import(EcsPipeline.Builder b)
    {
        b
            .AddRunner<EcsPausableRunner>()
            .AddRunner<EcsRunParallelRunner>()
            .AddRunner<PausableLateRunner>()
            .AddRunner<EcsRunLateRunner>()
            .AddRunner<EcsFixedRunner>()
            .Layers.Add(RunnerLayer).Before(EcsConsts.END_LAYER).Back
            .Add(new RunnerSystem(), RunnerLayer, -10000)
            .Add(new DestroySystem());
    }
}