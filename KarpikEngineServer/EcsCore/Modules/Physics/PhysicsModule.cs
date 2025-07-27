using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Server;

public class PhysicsModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new UpdateBox2DSystem())
            .AddCaller<CollisionsEvent>(EcsConsts.END_LAYER);
    }
}