using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Physics.Core;

internal class Physics2DModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<Physics2DBodyRestoreSystem>();
        systems.Add<Physics2DBodyCreator>(EcsConsts.PRE_BEGIN_LAYER);
        systems.Add<Physics2DBodyDestroyer>(EcsConsts.POST_END_LAYER);
        systems.Add<PhysicsPushSystem>(EcsConsts.PRE_BEGIN_LAYER);
        systems.Add<PhysicsStepSystem>();
        systems.Add<PhysicsPullSystem>(EcsConsts.POST_END_LAYER);
    }
}
