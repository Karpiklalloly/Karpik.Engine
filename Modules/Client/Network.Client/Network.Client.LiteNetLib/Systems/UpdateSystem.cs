using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.Client.Network.Core.Systems;

internal class InitNetworkClientSystem : IEcsInit
{
    [DI] private INetworkManager _manager;
    
    public void Init()
    {
        _manager.Start(_manager.GetFreePort());
        _manager.Connect("localhost", 9051, "MyGame");
        _manager.NetworkReceiveEvent += ManagerOnNetworkReceiveEvent;
        _manager.PeerConnectedEvent += ManagerOnPeerConnectedEvent;
        _manager.PeerDisconnectedEvent += ManagerOnPeerDisconnectedEvent;
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
    [DI] private INetworkManager _manager;
    
    public void Run()
    {
        _manager.PollEvents();
    }
}

internal class DestroyNetworkClientSystem : IEcsDestroy
{
    [DI] private INetworkManager _manager;
    
    public void Destroy()
    {
        _manager.Stop();
    }
}