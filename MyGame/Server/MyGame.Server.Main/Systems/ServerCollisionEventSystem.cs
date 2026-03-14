using System.Numerics;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

/// <summary>
/// Server-side collision event system - handles collectibles, death zones, finish zones
/// </summary>
public class ServerCollisionEventSystem : IEcsRun
{
    class PlayerAspect : EcsAspect
    {
        public EcsReadonlyPool<Player> Player = Inc;
    }
    
    class CollectibleAspect : EcsAspect
    {
        public EcsReadonlyPool<Collectible> Collectible = Inc;
    }
    
    [DI] private IPhysicsWorld2D _physicsWorld = null!;
    [DI] private EcsDefaultWorld _world = null!;

    public void Run()
    {
        // Get collision events from physics world
        var collisions = _physicsWorld.GetFrameCollisions();
        
        // Track which entities need to be destroyed
        var entitiesToDestroy = new List<int>();
        
        // Process each collision
        for (int i = 0; i < collisions.Length; i++)
        {
            var collision = collisions[i];
            ProcessCollision(collision.EntityA, collision.EntityB, collision.Normal, entitiesToDestroy);
        }
        
        // Destroy collected entities
        foreach (var entity in entitiesToDestroy)
        {
            if (_world.GetEntityLong(entity).IsAlive)
            {
                _world.DelEntity(entity);
            }
        }
    }
    
    private void ProcessCollision(int entityA, int entityB, Vector2 normal, List<int> entitiesToDestroy)
    {
        // Check if either entity is a player
        bool aIsPlayer = _world.GetPool<Player>().Has(entityA);
        bool bIsPlayer = _world.GetPool<Player>().Has(entityB);
        
        if (!aIsPlayer && !bIsPlayer)
            return;
            
        int playerEntity = aIsPlayer ? entityA : entityB;
        int otherEntity = aIsPlayer ? entityB : entityA;
        
        // Check for grounded state - if collision normal points up (Y > 0), player is on top of something
        if (normal.Y > 0.5f)
        {
            if (_world.GetPool<JumpState>().Has(playerEntity))
            {
                ref var jumpState = ref _world.GetPool<JumpState>().Get(playerEntity);
                jumpState.IsGrounded = true;
                jumpState.CanJump = true;
            }
        }
        
        // Check what we collided with
        if (_world.GetPool<Collectible>().Has(otherEntity))
        {
            // Collect the item
            ref var collectible = ref _world.GetPool<Collectible>().Get(otherEntity);
            if (!collectible.IsCollected)
            {
                collectible.IsCollected = true;
                // Mark entity for destruction
                if (!entitiesToDestroy.Contains(otherEntity))
                {
                    entitiesToDestroy.Add(otherEntity);
                }
            }
        }
    }
}
