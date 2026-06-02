using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Server.Main;

/// <summary>
/// Dispatches incoming commands from clients to server systems
/// </summary>
public partial class CommandDispatcher : IOnInjectedDI
{
    [DI] private EcsDefaultWorld _world = null!;
    [DI] private Time _time = null!;
    
    public void OnInjected()
    {
        // Register handlers for platformer input commands
    }
}
