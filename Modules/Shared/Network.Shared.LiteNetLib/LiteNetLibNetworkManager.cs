using System.Collections.Concurrent;
using System.Composition;
using System.Net;
using System.Net.Sockets;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using DeliveryMethod = Karpik.Engine.Shared.Network.Core.DeliveryMethod;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

[Export(typeof(INetworkManager))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class LiteNetLibNetworkManager : INetworkManager
{
    public event INetworkManager.NetworkEventHandler? NetworkReceiveEvent;
    public event INetworkManager.PeerConnectionEventHandler? PeerConnectedEvent;
    public event INetworkManager.PeerDisconnectionEventHandler? PeerDisconnectedEvent;
    public event INetworkManager.ConnectionRequestEventHandler? ConnectionRequestEvent;

    public NetManager Manager { get; }

    private readonly EventBasedNetListener _listener;
    private readonly ConcurrentDictionary<NetPeer, IPeer> _peers = new();
    private readonly ConcurrentDictionary<NetPeer, byte> _schemaValidatedPeers = new();
    private long _protocolSchemaHash;
    private bool _protocolSchemaConfigured;
    private bool _disposed = false;

    public LiteNetLibNetworkManager()
    {
        _listener = new EventBasedNetListener();
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.ConnectionRequestEvent += ListenerOnConnectionRequestEvent;
        Manager = new NetManager(_listener);
    }

    public IPeer? FirstPeer
    {
        get
        {
            if (_peers.IsEmpty) return null;
            if (Manager.FirstPeer is null) return null;

            return _peers[Manager.FirstPeer];
        }
    }

    public int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public void Start(int port)
    {
        if (!_protocolSchemaConfigured)
        {
            throw new InvalidOperationException("A nonzero protocol schema must be configured before the network manager starts.");
        }

        bool started = port == 0
            ? Manager.Start()
            : Manager.Start(port);
        if (!started)
        {
            throw new InvalidOperationException(
                port == 0
                    ? "LiteNetLib failed to bind an available UDP port."
                    : $"LiteNetLib failed to bind UDP port {port}.");
        }
    }

    public void ConfigureProtocolSchema(long schemaHash)
    {
        if (schemaHash == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaHash),
                "Protocol schema hash zero is reserved for an unconfigured manager.");
        }

        if (Manager.IsRunning)
        {
            throw new InvalidOperationException("The protocol schema must be configured before the network manager starts.");
        }

        _protocolSchemaHash = schemaHash;
        _protocolSchemaConfigured = true;
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
        if (Manager.IsRunning)
        {
            Manager.DisconnectAll();
            Manager.Stop();
        }

        _peers.Clear();
        _schemaValidatedPeers.Clear();
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
        if (!_peers.TryGetValue(peer, out var wrappedPeer))
        {
            wrappedPeer = new LiteNetLibPeer(peer);
            _peers.TryAdd(peer, wrappedPeer);
        }

        if (reader.AvailableBytes == 0)
        {
            peer.Disconnect();
            reader.Recycle();
            return;
        }

        if ((PacketType)reader.PeekByte() == PacketType.Handshake)
        {
            reader.GetByte();
            if (reader.AvailableBytes != sizeof(long))
            {
                peer.Disconnect();
                reader.Recycle();
                return;
            }

            var handshakeResult = NetworkSchemaHandshake.ValidatePayload(
                new LiteNetLibReader(reader),
                _protocolSchemaHash,
                out _);
            if (handshakeResult != NetworkSchemaHandshakeResult.Accepted)
            {
                peer.Disconnect();
                reader.Recycle();
                return;
            }

            if (_schemaValidatedPeers.TryAdd(peer, 0))
            {
                PeerConnectedEvent?.Invoke(wrappedPeer);
            }
            reader.Recycle();
            return;
        }

        if (!_schemaValidatedPeers.ContainsKey(peer))
        {
            peer.Disconnect();
            reader.Recycle();
            return;
        }

        NetworkReceiveEvent?.Invoke(wrappedPeer, new LiteNetLibReader(reader), channel, (DeliveryMethod)deliveryMethod);
    }
    
    private void OnPeerConnected(NetPeer peer)
    {
        var wrappedPeer = new LiteNetLibPeer(peer);
        _peers.TryAdd(peer, wrappedPeer);
        var writer = new LiteNetLibWriter(new NetDataWriter());
        NetworkSchemaHandshake.Write(writer, _protocolSchemaHash);
        wrappedPeer.Send(writer, DeliveryMethod.ReliableOrdered);
    }
    
    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (_peers.TryRemove(peer, out var wrappedPeer))
        {
            _schemaValidatedPeers.TryRemove(peer, out _);
            PeerDisconnectedEvent?.Invoke(wrappedPeer, new LiteNetLibDisconnectInfo(disconnectInfo));
        }
    }
    
    private void ListenerOnConnectionRequestEvent(ConnectionRequest request)
    {
        ConnectionRequestEvent?.Invoke(new LiteNetLibConnectionRequest(request));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Stop();

        _listener.NetworkReceiveEvent -= OnNetworkReceive;
        _listener.PeerConnectedEvent -= OnPeerConnected;
        _listener.PeerDisconnectedEvent -= OnPeerDisconnected;
        _listener.ConnectionRequestEvent -= ListenerOnConnectionRequestEvent;

        _listener.ClearNetworkReceiveEvent();
        _listener.ClearNetworkReceiveUnconnectedEvent();

        NetworkReceiveEvent = null;
        PeerConnectedEvent = null;
        PeerDisconnectedEvent = null;
        ConnectionRequestEvent = null;
    }
}
