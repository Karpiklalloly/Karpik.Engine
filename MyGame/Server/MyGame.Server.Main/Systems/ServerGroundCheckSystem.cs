using System.Numerics;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

public class ServerGroundCheckSystem : ISystemUpdate
{
    class PlayerAspect : EcsAspect
    {
        public EcsReadonlyPool<Player> Player = Inc;
        public EcsPool<Transform2D> Transform = Inc;
        public EcsPool<PhysicsBodyRef> Body = Opt;
        public EcsPool<KinematicCharacterController> Controller = Inc;
    }

    private const float GROUND_CHECK_DISTANCE = 0.1f;
    
    [DI] private IPhysicsWorld2D _physicsWorld = null!;
    [DI] private EcsDefaultWorld _world = null!;

    public void Update()
    {
        Span<RaycastHit2D> hits = stackalloc RaycastHit2D[4];
        
        foreach (var entity in _world.Where(out PlayerAspect a))
        {
            ref var transform = ref a.Transform.Get(entity);
            ref var controller = ref a.Controller.Get(entity);
            
            Vector2 rayStart = transform.Position;
            Vector2 rayEnd = rayStart + new Vector2(0, -1) * (1.0f + GROUND_CHECK_DISTANCE);
            
            int hitCount = _physicsWorld.Raycast(rayStart, rayEnd, Physics2DLayers.Platform, hits);
            
            bool wasGrounded = controller.IsGrounded;
            if (hitCount > 0)
            {
                Console.WriteLine($"Hits {hitCount}");
            }
            controller.IsGrounded = hitCount > 0;
            
            if (!wasGrounded && controller.IsGrounded)
            {
                Console.WriteLine($"Player {entity} landed");
            }
            else if (wasGrounded && !controller.IsGrounded)
            {
                Console.WriteLine($"Player {entity} jumped/fell");
            }
        }
    }
}
