using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib.Configs;

namespace Network.Server.LiteNetLib.Systems;

internal class InitNetworkClientSystem : ISystemInit
{
    [DI] private INetworkManager _manager = null!;
    [DI] private NetworkConfig _config = null!;
    
    public void Init()
    {
        _manager.Start(_config.Port);
        _manager.NetworkReceiveEvent += ManagerOnNetworkReceiveEvent;
        _manager.PeerConnectedEvent += ManagerOnPeerConnectedEvent;
        _manager.PeerDisconnectedEvent += ManagerOnPeerDisconnectedEvent;
        _manager.ConnectionRequestEvent += ManagerOnConnectionRequestEvent;
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

internal class UpdateNetworkClientSystem : ISystemBegin
{
    [DI] private INetworkManager _manager = null!;
    
    public void Begin()
    {
        _manager.PollEvents();
    }
}

internal class DestroyNetworkClientSystem : ISystemDestroy
{
    [DI] private INetworkManager _manager = null!;
    
    public void Destroy()
    {
        _manager.NetworkReceiveEvent -= InitNetworkClientSystem.ManagerOnNetworkReceiveEvent;
        _manager.PeerConnectedEvent -= InitNetworkClientSystem.ManagerOnPeerConnectedEvent;
        _manager.PeerDisconnectedEvent -= InitNetworkClientSystem.ManagerOnPeerDisconnectedEvent;
        _manager.ConnectionRequestEvent -= InitNetworkClientSystem.ManagerOnConnectionRequestEvent;
        _manager.Stop();
    }
}