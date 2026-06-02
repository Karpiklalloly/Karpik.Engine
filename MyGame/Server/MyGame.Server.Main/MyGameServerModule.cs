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
        b.Add(new KinematicControllerSystem(), EcsConsts.PRE_BEGIN_LAYER);
        
        // Collision events - collectibles, death zones, finish
        b.Add(new ServerCollisionEventSystem());
        
        // Respawn - handle player death and respawn
        b.AddCaller<PlatformerInputCommand>();
    }
}
