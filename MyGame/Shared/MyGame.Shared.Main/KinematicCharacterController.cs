using System.Numerics;
using DCFApixels.DragonECS;

namespace Karpik.Engine.MyGame.Shared.Main;

/// <summary>
/// Kinematic character controller state - controls platformer movement
/// </summary>
public struct KinematicCharacterController : IEcsComponent
{
    /// <summary>
    /// Horizontal movement speed (units per second)
    /// </summary>
    public float MoveSpeed;
    
    /// <summary>
    /// Jump impulse force (units per second)
    /// </summary>
    public float JumpForce;
    
    /// <summary>
    /// Gravity acceleration (units per second squared)
    /// </summary>
    public float Gravity;
    
    /// <summary>
    /// Maximum fall speed (units per second)
    /// </summary>
    public float MaxFallSpeed;
    
    /// <summary>
    /// Current grounded state - true if player is standing on something
    /// </summary>
    public bool IsGrounded;
    
    /// <summary>
    /// Time when jump was last performed
    /// </summary>
    public float LastJumpTime;
    
    /// <summary>
    /// Minimum time between jumps (cooldown)
    /// </summary>
    public float JumpCooldown;
}

public struct WannaMove : IEcsComponent
{
    public float MoveX;
    public bool Jump;
}
