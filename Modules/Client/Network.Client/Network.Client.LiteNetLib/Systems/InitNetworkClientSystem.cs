using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.Client.Network.LiteNetLib.Systems;

internal class InitNetworkClientSystem(
    INetworkManager manager,
    INetworkProtocolSchema protocolSchema,
    NetworkConfig config) : ISystemInit
{
    public void Init()
    {
        manager.ConfigureProtocolSchema(protocolSchema.ProtocolSchemaHash);
        manager.Start(0);
        manager.Connect(config.Address, config.Port, config.Key);
        manager.NetworkReceiveEvent += ManagerOnNetworkReceiveEvent;
        manager.PeerConnectedEvent += ManagerOnPeerConnectedEvent;
        manager.PeerDisconnectedEvent += ManagerOnPeerDisconnectedEvent;
    }
    
    internal static void ManagerOnNetworkReceiveEvent(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        
    }
    
    internal static void ManagerOnPeerConnectedEvent(IPeer peer)
    {
        Console.WriteLine("OnPeerConnected");
    }
    
    internal static void ManagerOnPeerDisconnectedEvent(IPeer peer, IDisconnectInfo info)
    {
        Console.WriteLine("OnPeerDisconnected");
    }
}

public class DestroyNetworkClientSystem(INetworkManager manager) : ISystemDestroy
{
    public void Destroy()
    {
        manager.NetworkReceiveEvent -= InitNetworkClientSystem.ManagerOnNetworkReceiveEvent;
        manager.PeerConnectedEvent -= InitNetworkClientSystem.ManagerOnPeerConnectedEvent;
        manager.PeerDisconnectedEvent -= InitNetworkClientSystem.ManagerOnPeerDisconnectedEvent;
        manager.Stop();
    }
}
