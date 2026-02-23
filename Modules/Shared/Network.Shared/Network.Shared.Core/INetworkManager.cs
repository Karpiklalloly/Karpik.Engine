namespace Karpik.Engine.Shared.Network.Core;

public interface INetworkManager
{
    public delegate void NetworkEventHandler(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod);
    public delegate void PeerConnectionEventHandler(IPeer peer);
    public delegate void PeerDisconnectionEventHandler(IPeer peer, IDisconnectInfo info);
    public delegate void ConnectionRequestEventHandler(IConnectionRequest request);
    
    public event NetworkEventHandler NetworkReceiveEvent;
    public event PeerConnectionEventHandler PeerConnectedEvent;
    public event PeerDisconnectionEventHandler PeerDisconnectedEvent;
    public event ConnectionRequestEventHandler ConnectionRequestEvent;
    
    public IPeer? FirstPeer { get; }
    
    public int GetFreePort();
    public void Start(int port);
    public void Connect(string address, int port, string key);
    public void PollEvents();
    public void Stop();
    public void SendToAll(IWriter writer, DeliveryMethod deliveryMethod);
    
    public IWriter CreateWriter();
}