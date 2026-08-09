using Karpik.Engine.Core;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Shared.Physics.Core;

public class PhysicsStepSystem(IPhysicsWorld2D physics, Time time) : ISystemFixedUpdate
{
    public void FixedUpdate()
    {
        physics.Step((float)time.FixedDeltaTime);
    }
}