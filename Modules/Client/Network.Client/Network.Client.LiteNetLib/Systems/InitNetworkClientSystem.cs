using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib.Configs;

namespace Karpik.Engine.Client.Network.LiteNetLib.Systems;

internal class InitNetworkClientSystem : IEcsInit, IEcsDestroy
{
    [DI] private INetworkManager _manager = null!;
    [DI] private NetworkConfig _config = null!;
    
    public void Init()
    {
        _manager.Start(_manager.GetFreePort());
        _manager.Connect(_config.Address, _config.Port, _config.Key);
        _manager.NetworkReceiveEvent += ManagerOnNetworkReceiveEvent;
        _manager.PeerConnectedEvent += ManagerOnPeerConnectedEvent;
        _manager.PeerDisconnectedEvent += ManagerOnPeerDisconnectedEvent;
    }
    
    public void Destroy()
    {
        _manager.NetworkReceiveEvent -= ManagerOnNetworkReceiveEvent;
        _manager.PeerConnectedEvent -= ManagerOnPeerConnectedEvent;
        _manager.PeerDisconnectedEvent -= ManagerOnPeerDisconnectedEvent;
        _manager.Stop();
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