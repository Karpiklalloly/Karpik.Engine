using Karpik.Engine.MyGame.Server.Main.Systems;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main;

/// <summary>
/// Server module for MyGame - includes physics and game logic
/// </summary>
internal class MyGameServerModule : IModule
{
    public void Import(IBuilder b)
    {
        // Level initialization - create platforms and spawn points
        b.Add(new LevelInitSystem());
        
        // Network system - handles client connections and snapshots
        b.Add((object)new NetworkSystem());
        
        // Player input - applies forces based on PlayerInputState
        b.Add(new InputSystem());
        b.Add(new KinematicControllerSystem());
        
        // Ground check - updates JumpState based on velocity/collisions
        b.Add(new ServerGroundCheckSystem());
        
        // Collision events - collectibles, death zones, finish
        b.Add(new ServerCollisionEventSystem());
        
        // Respawn - handle player death and respawn
        b.Add(new ServerRespawnSystem())
            .AddCaller<PlatformerInputCommand>();
    }
}
