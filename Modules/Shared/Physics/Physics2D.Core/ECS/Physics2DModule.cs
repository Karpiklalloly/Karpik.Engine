using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Physics.Core;

internal class Physics2DModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new Physics2DBodyCreator(), EcsConsts.PRE_BEGIN_LAYER);
        b.Add(new Physics2DBodyDestroyer(), EcsConsts.POST_END_LAYER);
        b.Add(new PhysicsPushSystem(), EcsConsts.PRE_BEGIN_LAYER); // ECS -> Physics
        b.Add(new PhysicsStepSystem()); // Step()
        b.Add(new PhysicsPullSystem(), EcsConsts.POST_END_LAYER); // Physics -> ECS
    }
}