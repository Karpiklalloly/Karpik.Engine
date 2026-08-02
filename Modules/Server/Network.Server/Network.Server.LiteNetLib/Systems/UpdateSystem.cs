using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib.Configs;

namespace Network.Server.LiteNetLib.Systems;

internal class InitNetworkClientSystem(INetworkManager manager, NetworkConfig config) : ISystemInit
{
    public void Init()
    {
        manager.NetworkReceiveEvent += ManagerOnNetworkReceiveEvent;
        manager.PeerConnectedEvent += ManagerOnPeerConnectedEvent;
        manager.PeerDisconnectedEvent += ManagerOnPeerDisconnectedEvent;
        manager.ConnectionRequestEvent += ManagerOnConnectionRequestEvent;
        manager.Start(config.Port);
    }

    internal static void ManagerOnConnectionRequestEvent(IConnectionRequest request)
    {
        Console.WriteLine("OnConnectionRequest");
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

internal class UpdateNetworkClientSystem(INetworkManager manager) : ISystemBegin
{
    public void Begin()
    {
        manager.PollEvents();
    }
}

internal class DestroyNetworkClientSystem(INetworkManager manager) : ISystemDestroy
{
    public void Destroy()
    {
        manager.NetworkReceiveEvent -= InitNetworkClientSystem.ManagerOnNetworkReceiveEvent;
        manager.PeerConnectedEvent -= InitNetworkClientSystem.ManagerOnPeerConnectedEvent;
        manager.PeerDisconnectedEvent -= InitNetworkClientSystem.ManagerOnPeerDisconnectedEvent;
        manager.ConnectionRequestEvent -= InitNetworkClientSystem.ManagerOnConnectionRequestEvent;
        manager.Stop();
    }
}