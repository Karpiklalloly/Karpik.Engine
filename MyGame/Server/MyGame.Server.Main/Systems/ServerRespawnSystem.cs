using System.Numerics;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

/// <summary>
/// Server-side respawn system - handles player respawn when they fall off or hit death zone
/// </summary>
public class ServerRespawnSystem : IEcsRun
{
    class PlayerAspect : EcsAspect
    {
        public EcsReadonlyPool<Player> Player = Inc;
        public EcsPool<Transform2D> transform = Inc;
    }
    
    class RespawnAspect : EcsAspect
    {
        public EcsPool<RespawnPoint> RespawnPoint = Inc;
        public EcsReadonlyPool<Transform2D> transform = Inc;
    }

    private const float DEATH_Y = -10.0f;
    
    [DI] private EcsDefaultWorld _world = null!;

    public void Run()
    {
        foreach (var playerEntity in _world.Where(out PlayerAspect a))
        {
            ref var transform = ref a.transform.Get(playerEntity);
            
            // // Check if player fell too far
            // if (transform.Position.Y < DEATH_Y)
            // {
            //     RespawnPlayer(playerEntity, ref transform);
            // }
            //
            // // Check if player is out of horizontal bounds
            // if (transform.Position.X < -15 || transform.Position.X > 20)
            // {
            //     RespawnPlayer(playerEntity, ref transform);
            // }
        }
    }
    
    private void RespawnPlayer(int entity, ref Transform2D transformPlayer)
    {
        // Find respawn point
        Vector2 respawnPos = new Vector2(0, 3); // Default respawn
        
        foreach (var e in _world.Where(out RespawnAspect a))
        {
            // Use Transform2D for position, not RespawnPoint
            ref readonly var transform = ref a.transform.Get(e);
            respawnPos = transform.Position;
            break; // Use first respawn point
        }
        
        // Reset position
        transformPlayer.Position = respawnPos;
        transformPlayer.Rotation = 0;
        
        // Reset velocity
        if (_world.GetPool<Velocity2D>().Has(entity))
        {
            ref var velocity = ref _world.GetPool<Velocity2D>().Get(entity);
            velocity.Linear = Vector2.Zero;
            
            // Request physics to update velocity
            ref var velRequest = ref _world.GetPool<SetVelocityRequest>().TryAddOrGet(entity);
            velRequest.Linear = Vector2.Zero;
        }
        
        // Reset input state
        if (_world.GetPool<PlayerInputState>().Has(entity))
        {
            ref var inputState = ref _world.GetPool<PlayerInputState>().Get(entity);
            // inputState.MoveX = 0;
            // inputState.Jump = false;
        }
    }
}
