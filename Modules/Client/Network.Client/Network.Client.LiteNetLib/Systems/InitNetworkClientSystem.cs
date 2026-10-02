using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Client.Network.LiteNetLib.Systems;

public class InitNetworkClientSystem(
    INetworkManager manager,
    INetworkProtocolSchema protocolSchema,
    NetworkConfig config) : ISystemInit
{
    public void Init()
    {
        manager.ConfigureProtocolSchema(protocolSchema.ProtocolSchemaHash);
        manager.Start(0);
        manager.Connect(config.Address, config.Port, config.Key);
    }
}

public class DestroyNetworkClientSystem : ISystemDestroy
{
    private readonly INetworkManager _manager;
    private readonly ILogger<DestroyNetworkClientSystem> _logger;

    public DestroyNetworkClientSystem(INetworkManager manager, ILogger<DestroyNetworkClientSystem> logger)
    {
        _manager = manager;
        _logger = logger;
        manager.NetworkReceiveEvent += ManagerOnNetworkReceiveEvent;
        manager.PeerConnectedEvent += ManagerOnPeerConnectedEvent;
        manager.PeerDisconnectedEvent += ManagerOnPeerDisconnectedEvent;
    }

    private void ManagerOnNetworkReceiveEvent(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
    }

    private void ManagerOnPeerConnectedEvent(IPeer peer)
    {
        _logger.LogInformation("Peer {PeerId} connected", peer.Id);
    }

    private void ManagerOnPeerDisconnectedEvent(IPeer peer, IDisconnectInfo info)
    {
        _logger.LogInformation("Peer {PeerId} disconnected", peer.Id);
    }

    public void Destroy()
    {
        _manager.NetworkReceiveEvent -= ManagerOnNetworkReceiveEvent;
        _manager.PeerConnectedEvent -= ManagerOnPeerConnectedEvent;
        _manager.PeerDisconnectedEvent -= ManagerOnPeerDisconnectedEvent;
        _manager.Stop();
    }
}
