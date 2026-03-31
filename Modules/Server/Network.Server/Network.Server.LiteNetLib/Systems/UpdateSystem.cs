using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib.Configs;

namespace Network.Server.LiteNetLib.Systems;

internal class InitNetworkClientSystem : IEcsInit, IEcsDestroy
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
    
    public void Destroy()
    {
        _manager.NetworkReceiveEvent -= ManagerOnNetworkReceiveEvent;
        _manager.PeerConnectedEvent -= ManagerOnPeerConnectedEvent;
        _manager.PeerDisconnectedEvent -= ManagerOnPeerDisconnectedEvent;
        _manager.ConnectionRequestEvent -= ManagerOnConnectionRequestEvent;
    }

    private void ManagerOnConnectionRequestEvent(IConnectionRequest request)
    {
        Console.WriteLine("OnConnectionRequest");
    }

    private void ManagerOnNetworkReceiveEvent(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        
    }
    
    private void ManagerOnPeerConnectedEvent(IPeer peer)
    {
        Console.WriteLine("OnPeerConnected");
    }
    
    private void ManagerOnPeerDisconnectedEvent(IPeer peer, IDisconnectInfo info)
    {
        Console.WriteLine("OnPeerDisconnected");
    }
}

internal class UpdateNetworkClientSystem : IEcsRun
{
    [DI] private INetworkManager _manager = null!;
    
    public void Run()
    {
        _manager.PollEvents();
    }
}

internal class DestroyNetworkClientSystem : IEcsDestroy
{
    [DI] private INetworkManager _manager = null!;
    
    public void Destroy()
    {
        _manager.Stop();
    }
}