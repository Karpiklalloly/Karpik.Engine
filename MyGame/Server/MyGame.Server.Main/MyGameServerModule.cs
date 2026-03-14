using Karpik.Engine.MyGame.Server.Main.Systems;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main;

/// <summary>
/// Server module for MyGame - includes physics and game logic
/// </summary>
internal class MyGameServerModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        // Network system - handles client connections and snapshots
        b.Add(new NetworkSystem());
        
        // Player input - applies forces based on PlayerInputState
        b.Add(new ServerPlayerInputSystem());
        
        // Ground check - updates JumpState based on velocity/collisions
        b.Add(new ServerGroundCheckSystem());
        
        // Collision events - collectibles, death zones, finish
        b.Add(new ServerCollisionEventSystem());
        
        // Respawn - handle player death and respawn
        b.Add(new ServerRespawnSystem());
    }
}
