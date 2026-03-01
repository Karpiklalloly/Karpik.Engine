using Karpik.Engine.Core;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Shared.Physics.Core;

public class PhysicsStepSystem : IEcsFixedRun
{
    [DI] private IPhysicsWorld2D _physics = null!;
    [DI] private Time _time = null!;
    
    public void FixedRun()
    {
        _physics.Step((float)_time.FixedDeltaTime);
    }
}