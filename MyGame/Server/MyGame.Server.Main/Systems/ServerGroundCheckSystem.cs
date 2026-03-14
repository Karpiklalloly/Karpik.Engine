using System.Numerics;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

/// <summary>
/// Server-side ground check system - checks if player is on ground using physics collisions
/// </summary>
public class ServerGroundCheckSystem : IEcsRun
{
    class PlayerAspect : EcsAspect
    {
        public EcsReadonlyPool<Player> Player = Inc;
        public EcsPool<JumpState> jumpState = Opt;
        public EcsPool<Velocity2D> velocity = Opt;
    }

    private const float GROUNDED_VELOCITY_THRESHOLD = 0.1f;
    
    [DI] private IPhysicsWorld2D _physicsWorld = null!;
    [DI] private EcsDefaultWorld _world = null!;

    public void Run()
    {
        // Get collision events from physics world
        var collisions = _physicsWorld.GetFrameCollisions();
        
        // Reset all players' grounded state first
        foreach (var entity in _world.Where(out PlayerAspect a))
        {
            if (_world.GetPool<JumpState>().Has(entity))
            {
                ref var jumpState = ref _world.GetPool<JumpState>().Get(entity);
                jumpState.IsGrounded = false;
            }
        }
        
        // Process each collision to determine grounded state
        for (int i = 0; i < collisions.Length; i++)
        {
            var collision = collisions[i];
            ProcessGroundedCheck(collision.EntityA, collision.EntityB, collision.Normal);
        }
        
        // Also check velocity for players that might be on ground without collision events
        foreach (var entity in _world.Where(out PlayerAspect a))
        {
            if (!_world.GetPool<JumpState>().Has(entity))
                continue;
                
            ref var jumpState = ref _world.GetPool<JumpState>().Get(entity);
            
            if (!_world.GetPool<Velocity2D>().Has(entity))
                continue;
                
            ref var velocity = ref _world.GetPool<Velocity2D>().Get(entity);
            
            // If velocity Y is positive (falling) and we're not marked as grounded,
            // we might have just hit ground - but wait for collision events
            // This is handled by collision events above
        }
    }
    
    private void ProcessGroundedCheck(int entityA, int entityB, Vector2 normal)
    {
        // Check if either entity is a player
        bool aIsPlayer = _world.GetPool<Player>().Has(entityA);
        bool bIsPlayer = _world.GetPool<Player>().Has(entityB);
        
        if (!aIsPlayer && !bIsPlayer)
            return;
            
        int playerEntity = aIsPlayer ? entityA : entityB;
        
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
    }
}
