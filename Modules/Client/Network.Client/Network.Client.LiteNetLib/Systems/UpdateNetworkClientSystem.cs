using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.Client.Network.LiteNetLib.Systems;

public class UpdateNetworkClientSystem(INetworkManager manager) : ISystemBegin
{
    public void Begin()
    {
        manager.PollEvents();
    }
}