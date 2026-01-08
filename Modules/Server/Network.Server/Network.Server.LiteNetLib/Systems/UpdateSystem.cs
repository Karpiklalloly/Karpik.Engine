using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;

namespace Network.Server.LiteNetLib.Systems;

internal class InitNetworkClientSystem : IEcsInit
{
    [DI] private INetworkManager _manager = null!;
    
    public void Init()
    {
        _manager.Start(9051);
        _manager.NetworkReceiveEvent += ManagerOnNetworkReceiveEvent;
        _manager.PeerConnectedEvent += ManagerOnPeerConnectedEvent;
        _manager.PeerDisconnectedEvent += ManagerOnPeerDisconnectedEvent;
        _manager.ConnectionRequestEvent += ManagerOnConnectionRequestEvent;
    }

    private void ManagerOnConnectionRequestEvent(IConnectionRequest request)
    {
        
    }

    private void ManagerOnNetworkReceiveEvent(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        
    }
    
    private void ManagerOnPeerConnectedEvent(IPeer peer)
    {
        
    }
    
    private void ManagerOnPeerDisconnectedEvent(IPeer peer, IDisconnectInfo info)
    {
        
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