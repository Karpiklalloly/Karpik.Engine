using System.Numerics;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

/// <summary>
/// Server-side player input system - applies forces based on PlayerInputState
/// </summary>
public class ServerPlayerInputSystem : IEcsRun
{
    class PlayerAspect : EcsAspect
    {
        public EcsReadonlyPool<Player> Player = Inc;
        public EcsPool<PlayerInputState> inputState = Inc;
        public EcsPool<JumpState> jumpState = Opt;
        public EcsPool<Velocity2D> velocity = Opt;
    }

    private const float MOVE_SPEED = 8.0f;
    private const float JUMP_FORCE = 12.0f;
    private const float MAX_VELOCITY_X = 10.0f;
    private const float JUMP_COOLDOWN = 0.2f;
    
    [DI] private EcsDefaultWorld _world = null!;
    [DI] private Time _time = null!;

    public void Run()
    {
        foreach (var entity in _world.Where(out PlayerAspect a))
        {
            ref var inputState = ref a.inputState.Get(entity);
            ref var jumpState = ref a.jumpState.TryAddOrGet(entity);
            ref var velocity = ref a.velocity.TryAddOrGet(entity);
            
            // Get current input
            float moveX = inputState.MoveX;
            bool jumpPressed = inputState.Jump;
            float currentTime = (float)_time.TotalTime;
            
            // Horizontal movement - apply directly for tighter platformer controls
            float newVelX = moveX * MOVE_SPEED;
            
            // Clamp horizontal velocity
            if (MathF.Abs(velocity.Linear.X) > MAX_VELOCITY_X)
            {
                newVelX = MathF.Sign(velocity.Linear.X) * MAX_VELOCITY_X;
            }
            
            // Check jump cooldown
            bool canJump = jumpState.CanJump && 
                          (currentTime - inputState.LastJumpTime > JUMP_COOLDOWN);
            
            // Jumping
            if (jumpPressed && canJump)
            {
                // Apply jump velocity (negative Y is up in most 2D systems)
                velocity.Linear = new Vector2(newVelX, -JUMP_FORCE);
                jumpState.LastJumpTime = currentTime;
                jumpState.IsGrounded = false;
                
                // Update input state with jump time
                inputState.LastJumpTime = currentTime;
                
                // Add velocity request to ensure physics processes it
                ref var velRequest = ref _world.GetPool<SetVelocityRequest>().TryAddOrGet(entity);
                velRequest.Linear = velocity.Linear;
            }
            else
            {
                // Just apply horizontal movement, preserve Y velocity (gravity)
                velocity.Linear = new Vector2(newVelX, velocity.Linear.Y);
                
                // Add velocity request
                ref var velRequest = ref _world.GetPool<SetVelocityRequest>().TryAddOrGet(entity);
                velRequest.Linear = velocity.Linear;
            }
            
            // Reset input state for next frame (commands are processed once)
            inputState.MoveX = 0;
            inputState.Jump = false;
        }
    }
}
