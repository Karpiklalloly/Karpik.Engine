using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.Client.Network.LiteNetLib.Systems;

internal class UpdateNetworkClientSystem : IEcsRun
{
    [DI] private INetworkManager _manager = null!;
    
    public void Run()
    {
        _manager.PollEvents();
    }
}