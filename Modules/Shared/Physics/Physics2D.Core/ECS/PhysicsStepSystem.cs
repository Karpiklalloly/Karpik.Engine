using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Physics.Core;

public class PhysicsStepSystem(IPhysicsWorld2D physics, Time time) : ISystemFixedUpdate
{
    public void FixedUpdate()
    {
        physics.Step(time.FixedDeltaTime);
    }
}