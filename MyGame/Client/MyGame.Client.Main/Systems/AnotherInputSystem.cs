using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

public class AnotherInputSystem : IEcsRun
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<Player> player = Inc;
        public EcsReadonlyPool<PhysicsBodyRef> body = Inc;
    }

    [DI] private IPhysicsWorld2D _physicsWorld2D;
    [DI] private EcsDefaultWorld _world;
    [DI] private Input _input;
    
    public void Run()
    {
        Vector2 pulse = Vector2.Zero;
        if (_input.IsDown(KeyboardKeys.Space))
        {
            pulse = new Vector2(1, 0.5f);
        }

        if (pulse != Vector2.Zero)
        {
            foreach (var e in _world.Where(out Aspect a))
            {
                ref var request = ref _world.GetPool<SetVelocityRequest>().TryAddOrGet(e);
                request.Linear = pulse * 5f; // Двигаемся со скоростью 5 м/с
            }
        }
    }
}