using System.Net;
using System.Net.Sockets;
using Karpik.Engine.Shared.Network.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using DeliveryMethod = Karpik.Engine.Shared.Network.Core.DeliveryMethod;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

public class LiteNetLibNetworkManager : INetworkManager
{
    public event INetworkManager.NetworkEventHandler? NetworkReceiveEvent;
    public event INetworkManager.PeerConnectionEventHandler? PeerConnectedEvent;
    public event INetworkManager.PeerDisconnectionEventHandler? PeerDisconnectedEvent;
    public event INetworkManager.ConnectionRequestEventHandler? ConnectionRequestEvent;

    public NetManager Manager { get; }

    private readonly EventBasedNetListener _listener;

    public LiteNetLibNetworkManager()
    {
        _listener = new EventBasedNetListener();
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.ConnectionRequestEvent += ListenerOnConnectionRequestEvent;
        Manager = new NetManager(_listener);
    }

    public IPeer FirstPeer => new LiteNetLibPeer(Manager.FirstPeer);

    public int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public void Start(int port)
    {
        Manager.Start(port);
    }

    public void Connect(string address, int port, string key)
    {
        Manager.Connect(address, port, key);
    }

    public void PollEvents()
    {
        Manager.PollEvents();
    }

    public void Stop()
    {
        _listener.ClearNetworkReceiveEvent();
        _listener.ClearNetworkReceiveUnconnectedEvent();
        Manager.Stop();
    }

    public void SendToAll(IWriter writer, DeliveryMethod deliveryMethod)
    {
        Manager.SendToAll(((LiteNetLibWriter)writer).Writer, (global::LiteNetLib.DeliveryMethod)deliveryMethod);
    }

    public IWriter CreateWriter()
    {
        return new LiteNetLibWriter(new NetDataWriter());
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, global::LiteNetLib.DeliveryMethod deliveryMethod)
    {
        NetworkReceiveEvent?.Invoke(new LiteNetLibPeer(peer), new LiteNetLibReader(reader), channel, (DeliveryMethod)deliveryMethod);
    }
    
    private void OnPeerConnected(NetPeer peer)
    {
        PeerConnectedEvent?.Invoke(new LiteNetLibPeer(peer));
    }
    
    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        PeerDisconnectedEvent?.Invoke(new LiteNetLibPeer(peer), new LiteNetLibDisconnectInfo(disconnectInfo));
    }
    
    private void ListenerOnConnectionRequestEvent(ConnectionRequest request)
    {
        ConnectionRequestEvent?.Invoke(new LiteNetLibConnectionRequest(request));
    }
}