using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Shared.Main;

/// <summary>
/// Player input command for platformer - sent from client to server
/// </summary>
[Serializable]
public struct PlatformerInputCommand : IStateCommand
{
    /// <summary>
    /// Movement direction: -1 for left, 0 for none, 1 for right
    /// </summary>
    public float MoveX;
    
    /// <summary>
    /// True if jump button is pressed
    /// </summary>
    public bool Jump;
    
    public int Source { get; set; }
    public int Target { get; set; }
}

/// <summary>
/// Player input state - stored on server to buffer input between snapshots
/// </summary>
[NetworkedComponent]
public struct PlayerInputState : IEcsComponent
{
    [NetworkedField]
    public float MoveX;
    
    [NetworkedField]
    public bool Jump;
    
    [NetworkedField]
    public float LastJumpTime;
}
