using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Composition;
using System.Net;
using System.Net.Sockets;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using DeliveryMethod = Karpik.Engine.Shared.Network.Core.DeliveryMethod;
using WireDelivery = LiteNetLib.DeliveryMethod;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

[Export(typeof(INetworkManager))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class LiteNetLibNetworkManager : IRevisionTransitionNetwork
{
    // Envelope types apply only after both ends negotiate v1; legacy application bytes are not reserved.
    private const byte RevisionGameplay = 3;
    private const byte RevisionControl = 4;
    internal const int GameplayHeaderBytes = 1 + sizeof(long);
    private const int ControlHeaderBytes = 2 + 2 * sizeof(long);
    private const int MaxControlPayloadBytes = IRevisionTransitionNetwork.MaxControlPayloadBytes;
    private static ReadOnlySpan<byte> CapabilityTrailer => "KPRKREV1"u8;

    public event INetworkManager.NetworkEventHandler? NetworkReceiveEvent;
    public event INetworkManager.PeerConnectionEventHandler? PeerConnectedEvent;
    public event INetworkManager.PeerDisconnectionEventHandler? PeerDisconnectedEvent;
    public event INetworkManager.ConnectionRequestEventHandler? ConnectionRequestEvent;
    public event IRevisionTransitionNetwork.RevisionControlHandler? RevisionControlReceived;

    private readonly NetManager _manager;
    private readonly EventBasedNetListener _listener;
    private readonly ConcurrentDictionary<NetPeer, LiteNetLibPeer> _peers = new(ReferenceEqualityComparer.Instance);
    // Topology changes only on the pump owner; iteration adds no broadcast lock or allocations.
    private readonly List<LiteNetLibPeer> _gameplayPeers = new();
    private readonly Queue<(LiteNetLibPeer Peer, DisconnectInfo Info)> _pendingDisconnects = new();
    private readonly LiteNetLibReader _gameplayReader = new();
    private readonly LiteNetLibWriter _systemWriter = new(new NetDataWriter());
    private long _protocolSchemaHash;
    private long _initialRevision;
    private bool _sendOpen = true;
    private bool _receiveOpen = true;
    private bool _polling;
    private bool _disposed;

    public LiteNetLibNetworkManager()
    {
        _listener = new EventBasedNetListener();
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _manager = new NetManager(_listener);
    }

    public long GameplayRevision { get; private set; }
    public bool GameplaySendingEnabled => _sendOpen;
    public bool GameplayReceivingEnabled => _receiveOpen;
    private bool RevisionEnabled => _initialRevision != 0;
    // Only the existing startup handshake is known here. A new live revision has no join contract in WP-05A.
    private bool _revisionCohortReserved;
    private bool CanHandshake => _sendOpen && _receiveOpen && GameplayRevision == _initialRevision && !_revisionCohortReserved;

    public bool CanBeginRevisionTransition
    {
        get
        {
            if (!RevisionEnabled || !_manager.IsRunning || !_sendOpen || !_receiveOpen ||
                _gameplayPeers.Count == 0 || _gameplayPeers.Count != _manager.ConnectedPeersCount)
                return false;

            foreach (var peer in _gameplayPeers)
                if (peer.WireMode != PeerWireMode.Revision || peer.RawPeer.ConnectionState != global::LiteNetLib.ConnectionState.Connected)
                    return false;
            return true;
        }
    }

    public IPeer? FirstPeer
    {
        get
        {
            if (!RevisionEnabled)
                return _manager.FirstPeer is { } raw && _peers.TryGetValue(raw, out var legacyPeer) ? legacyPeer : null;

            foreach (var peer in _gameplayPeers)
                if (peer.RawPeer.ConnectionState == global::LiteNetLib.ConnectionState.Connected)
                    return peer;
            return null;
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
        if (_protocolSchemaHash == 0)
            throw new InvalidOperationException("A nonzero protocol schema must be configured before the network manager starts.");

        bool started = port == 0 ? _manager.Start() : _manager.Start(port);
        if (!started)
            throw new InvalidOperationException(port == 0
                ? "LiteNetLib failed to bind an available UDP port."
                : $"LiteNetLib failed to bind UDP port {port}.");
    }

    public void ConfigureProtocolSchema(long schemaHash)
    {
        if (schemaHash == 0)
            throw new ArgumentOutOfRangeException(nameof(schemaHash), "Protocol schema hash zero is reserved for an unconfigured manager.");
        if (_manager.IsRunning)
            throw new InvalidOperationException("The protocol schema must be configured before the network manager starts.");
        _protocolSchemaHash = schemaHash;
    }

    public void ConfigureRevisionTransitions(long initialRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialRevision);
        if (_manager.IsRunning)
            throw new InvalidOperationException("Revision transitions must be configured before the network manager starts.");

        _initialRevision = initialRevision;
        GameplayRevision = initialRevision;
        _systemWriter.Writer.ResizeIfNeed(ControlHeaderBytes + MaxControlPayloadBytes);
    }

    public bool TryReserveRevisionCohort(Span<IPeer> peers, out int count)
    {
        count = 0;
        if (_revisionCohortReserved || !CanBeginRevisionTransition || peers.Length < _gameplayPeers.Count) return false;
        foreach (var peer in _gameplayPeers) peers[count++] = peer;
        return _revisionCohortReserved = true;
    }

    public void ReleaseRevisionCohort() => _revisionCohortReserved = false;

    public bool TrySetGameplayGates(bool sendOpen, bool receiveOpen)
    {
        if (!RevisionEnabled || !_manager.IsRunning ||
            (_sendOpen && _receiveOpen && (!sendOpen || !receiveOpen) && !_revisionCohortReserved && !CanBeginRevisionTransition))
            return false;
        _sendOpen = sendOpen;
        _receiveOpen = receiveOpen;
        return true;
    }

    public bool TryAdvanceGameplayRevision(long expectedRevision, long nextRevision)
    {
        if (_sendOpen || _receiveOpen || GameplayRevision != expectedRevision || nextRevision <= expectedRevision)
            return false;
        GameplayRevision = nextRevision;
        return true;
    }

    public void Connect(string address, int port, string key)
    {
        if (!RevisionEnabled)
        {
            _manager.Connect(address, port, key);
            return;
        }
        if (!CanHandshake)
            throw new InvalidOperationException("The startup handshake is unavailable during or after a live revision change.");

        var data = NetDataWriter.FromString(key);
        data.Put(CapabilityTrailer);
        if (_manager.Connect(address, port, data) is { } raw && !_peers.ContainsKey(raw))
            GetPeer(raw).WireMode = PeerWireMode.AwaitingServerHandshake;
    }

    public void PollEvents()
    {
        if (_polling)
            throw new InvalidOperationException("The transport pump cannot be entered recursively.");
        _polling = true;
        try
        {
            FlushGameplayDisconnects();
            _manager.PollEvents();
        }
        finally
        {
            _polling = false;
        }
    }

    public void Stop()
    {
        if (_manager.IsRunning)
        {
            _manager.DisconnectAll();
            _manager.Stop();
        }

        _peers.Clear();
        _gameplayPeers.Clear();
        _pendingDisconnects.Clear();
        _sendOpen = _receiveOpen = true;
        GameplayRevision = _initialRevision;
        ReleaseRevisionCohort();
    }

    public void SendToAll(IWriter writer, DeliveryMethod deliveryMethod)
    {
        if (!RevisionEnabled)
        {
            _manager.SendToAll(((LiteNetLibWriter)writer).GameplayPayload, (WireDelivery)deliveryMethod);
            return;
        }
        if (!_sendOpen)
            return;

        var packet = (LiteNetLibWriter)writer;
        var payload = packet.GameplayPayload;
        ReadOnlySpan<byte> revisionFrame = default;
        foreach (var peer in _gameplayPeers)
        {
            if (peer.WireMode != PeerWireMode.Revision)
                peer.RawPeer.Send(payload, (WireDelivery)deliveryMethod);
            else if (!payload.IsEmpty)
            {
                if (revisionFrame.IsEmpty) revisionFrame = packet.GetRevisionFrame(GameplayRevision);
                peer.RawPeer.Send(revisionFrame, 0, RevisionDelivery(deliveryMethod));
            }
        }
    }

    internal void SendGameplay(LiteNetLibPeer peer, IWriter writer, DeliveryMethod deliveryMethod)
    {
        if (RevisionEnabled && (!_sendOpen || !peer.SchemaValidated))
            return;
        var packet = (LiteNetLibWriter)writer;
        var payload = packet.GameplayPayload;
        if (peer.WireMode != PeerWireMode.Revision)
            peer.RawPeer.Send(payload, (WireDelivery)deliveryMethod);
        else if (!payload.IsEmpty)
            peer.RawPeer.Send(packet.GetRevisionFrame(GameplayRevision), 0, RevisionDelivery(deliveryMethod));
    }

    // The required lane is shared with control. Replaceable methods keep their independent delivery lanes.
    private static WireDelivery RevisionDelivery(DeliveryMethod method) =>
        method == DeliveryMethod.ReliableUnordered ? WireDelivery.ReliableOrdered : (WireDelivery)method;

    // Preserve the pinned writer's initial 64-byte payload capacity despite reserving framing space.
    public IWriter CreateWriter() => new LiteNetLibWriter(new NetDataWriter(true, 64 + GameplayHeaderBytes), reserveGameplayHeader: true);

    internal static void WriteGameplayHeader(Span<byte> frame, long revision)
    {
        frame[0] = RevisionGameplay;
        BinaryPrimitives.WriteInt64LittleEndian(frame[1..], revision);
    }

    public bool TrySendRevisionControl(IPeer peer, byte kind, long operationId, long revision, ReadOnlySpan<byte> payload)
    {
        if (kind == 0 || operationId <= 0 || revision <= 0 || payload.Length > MaxControlPayloadBytes ||
            peer is not LiteNetLibPeer target || target.Owner != this || !target.SchemaValidated ||
            target.WireMode != PeerWireMode.Revision || target.RawPeer.ConnectionState != global::LiteNetLib.ConnectionState.Connected)
            return false;

        var writer = _systemWriter.Writer;
        writer.SetPosition(ControlHeaderBytes);
        var header = writer.Data.AsSpan(0, ControlHeaderBytes);
        header[0] = RevisionControl;
        header[1] = kind;
        BinaryPrimitives.WriteInt64LittleEndian(header[2..], operationId);
        BinaryPrimitives.WriteInt64LittleEndian(header[(2 + sizeof(long))..], revision);
        writer.Put(payload);
        target.RawPeer.Send(writer, 0, WireDelivery.ReliableOrdered);
        return true;
    }

    private void OnNetworkReceive(NetPeer rawPeer, NetPacketReader reader, byte channel, WireDelivery delivery)
    {
        if (!RevisionEnabled)
        {
            var peer = GetPeer(rawPeer);
            if (reader.AvailableBytes != 0)
            {
                if (reader.PeekByte() == (byte)PacketType.Handshake)
                {
                    AcceptHandshake(peer, reader, channel, delivery);
                    reader.Recycle();
                    return;
                }
                if (peer.SchemaValidated)
                {
                    if (NetworkReceiveEvent is { } receive)
                        receive(peer, new LiteNetLibReader(reader), channel, (DeliveryMethod)delivery);
                    else
                        reader.Recycle();
                    return;
                }
            }
            rawPeer.Disconnect();
            reader.Recycle();
            return;
        }

        try
        {
            if (!_peers.TryGetValue(rawPeer, out var peer) || rawPeer.ConnectionState != global::LiteNetLib.ConnectionState.Connected)
                return;
            if (reader.AvailableBytes == 0)
            {
                rawPeer.Disconnect();
                return;
            }
            byte type = reader.PeekByte();
            if (type == (byte)PacketType.Handshake)
            {
                AcceptHandshake(peer, reader, channel, delivery);
                return;
            }
            if (!peer.SchemaValidated)
            {
                // Replaceable frames can overtake the schema echo; they are never proof of readiness.
                if (!IsReplaceable(delivery)) rawPeer.Disconnect();
                return;
            }

            if (peer.WireMode == PeerWireMode.Legacy)
            {
                DeliverGameplay(peer, reader, channel, delivery, 0);
                return;
            }
            if (channel != 0)
                return;
            if (type == RevisionControl)
            {
                if (delivery == WireDelivery.ReliableOrdered)
                    DeliverControl(peer, reader.PeekRemainingBytesSpan());
                return;
            }
            if (!_receiveOpen || type != RevisionGameplay || reader.AvailableBytes <= GameplayHeaderBytes ||
                delivery != WireDelivery.ReliableOrdered && !IsReplaceable(delivery))
                return;

            var revision = BinaryPrimitives.ReadInt64LittleEndian(reader.PeekRemainingBytesSpan()[1..]);
            if (revision != GameplayRevision)
                return;
            reader.SkipBytes(GameplayHeaderBytes);
            DeliverGameplay(peer, reader, channel, delivery, revision);
        }
        finally
        {
            reader.Recycle();
        }
    }

    private static bool IsReplaceable(WireDelivery delivery) => delivery is WireDelivery.Sequenced or WireDelivery.ReliableSequenced or WireDelivery.Unreliable;

    private void DeliverGameplay(LiteNetLibPeer peer, NetPacketReader reader, byte channel, WireDelivery delivery, long revision)
    {
        FlushGameplayDisconnects();
        if (!_receiveOpen || NetworkReceiveEvent is not { } receive || (revision != 0 && revision != GameplayRevision))
            return;

        // A single borrowed adapter; only the scalar revision may escape to deferred work.
        _gameplayReader.Bind(reader, revision);
        try { receive(peer, _gameplayReader, channel, (DeliveryMethod)delivery); }
        finally { _gameplayReader.Bind(null, 0); }
    }

    private void DeliverControl(LiteNetLibPeer peer, ReadOnlySpan<byte> frame)
    {
        if (frame.Length < ControlHeaderBytes || frame.Length > ControlHeaderBytes + MaxControlPayloadBytes || frame[1] == 0)
            return;
        var operation = BinaryPrimitives.ReadInt64LittleEndian(frame[2..]);
        var revision = BinaryPrimitives.ReadInt64LittleEndian(frame[(2 + sizeof(long))..]);
        if (operation > 0 && revision > 0)
            RevisionControlReceived?.Invoke(peer, frame[1], operation, revision, frame[ControlHeaderBytes..]);
    }

    private void AcceptHandshake(LiteNetLibPeer peer, NetPacketReader reader, byte channel, WireDelivery delivery)
    {
        reader.GetByte();
        bool revisionHandshake = reader.AvailableBytes == 2 * sizeof(long);
        bool matchingShape = peer.WireMode switch
        {
            PeerWireMode.Revision => revisionHandshake,
            PeerWireMode.AwaitingServerHandshake => revisionHandshake || reader.AvailableBytes == sizeof(long),
            _ => reader.AvailableBytes == sizeof(long),
        };
        if (!matchingShape || (RevisionEnabled && (channel != 0 || delivery != WireDelivery.ReliableOrdered)) ||
            reader.GetLong() != _protocolSchemaHash ||
            (revisionHandshake && BinaryPrimitives.ReadInt64LittleEndian(reader.PeekRemainingBytesSpan()) != _initialRevision))
        {
            peer.RawPeer.Disconnect();
            return;
        }
        if (peer.SchemaValidated)
            return;

        FlushGameplayDisconnects();
        if (!CanHandshake)
        {
            peer.RawPeer.Disconnect();
            return;
        }
        if (peer.WireMode == PeerWireMode.AwaitingServerHandshake)
        {
            // The initiator does not choose the mode based on its own advertisement. Its echo
            // enters the reliable ordered lane before any gameplay the connected callback sends.
            peer.WireMode = revisionHandshake ? PeerWireMode.Revision : PeerWireMode.Legacy;
            SendHandshake(peer);
        }
        peer.SchemaValidated = true;
        if (RevisionEnabled)
        {
            _gameplayPeers.Add(peer);
            _pendingDisconnects.EnsureCapacity(_gameplayPeers.Count);
        }
        PeerConnectedEvent?.Invoke(peer);
    }

    private void SendHandshake(LiteNetLibPeer peer)
    {
        NetworkSchemaHandshake.Write(_systemWriter, _protocolSchemaHash);
        var writer = _systemWriter.Writer;
        if (peer.WireMode == PeerWireMode.Revision)
        {
            BinaryPrimitives.WriteInt64LittleEndian(writer.Data.AsSpan(writer.Length), _initialRevision);
            writer.SetPosition(writer.Length + sizeof(long));
        }
        peer.RawPeer.Send(writer, 0, WireDelivery.ReliableOrdered);
    }

    private LiteNetLibPeer GetPeer(NetPeer peer)
    {
        if (_peers.TryGetValue(peer, out var wrapped))
            return wrapped;
        wrapped = new LiteNetLibPeer(this, peer);
        _peers.TryAdd(peer, wrapped);
        return wrapped;
    }

    private void OnPeerConnected(NetPeer rawPeer)
    {
        if (!CanHandshake)
        {
            rawPeer.Disconnect();
            return;
        }
        var peer = GetPeer(rawPeer);
        if (peer.WireMode != PeerWireMode.AwaitingServerHandshake)
            SendHandshake(peer);
    }

    private void OnPeerDisconnected(NetPeer rawPeer, DisconnectInfo info)
    {
        if (!RevisionEnabled)
        {
            if (rawPeer is not null && _peers.TryRemove(rawPeer, out var disconnected))
                PeerDisconnectedEvent?.Invoke(disconnected, new LiteNetLibDisconnectInfo(info));
            return;
        }

        // Do not retain the library's borrowed AdditionalData (nor an unadmitted peer) through maintenance.
        if (info.AdditionalData is { IsNull: false } additionalData) additionalData.Recycle();
        if (rawPeer is null || !_peers.TryRemove(rawPeer, out var peer) || !peer.SchemaValidated)
            return;
        peer.SchemaValidated = false;
        _gameplayPeers.Remove(peer);
        _pendingDisconnects.Enqueue((peer, new DisconnectInfo { Reason = info.Reason, SocketErrorCode = info.SocketErrorCode }));
        FlushGameplayDisconnects();
    }

    private void FlushGameplayDisconnects()
    {
        while (_receiveOpen && _pendingDisconnects.TryDequeue(out var entry))
            PeerDisconnectedEvent?.Invoke(entry.Peer, new LiteNetLibDisconnectInfo(entry.Info));
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        FlushGameplayDisconnects();
        if (!CanHandshake)
        {
            request.Reject();
            return;
        }
        ConnectionRequestEvent?.Invoke(new LiteNetLibConnectionRequest(this, request));
    }

    internal IPeer? AcceptIfKey(ConnectionRequest request, string key)
    {
        // Recheck in case the caller retained a request across a gate change.
        if (!CanHandshake)
        {
            request.Reject();
            return null;
        }
        if (request.AcceptIfKey(key) is not { } rawPeer)
            return null;
        var peer = GetPeer(rawPeer);
        // LiteNetLib consumes only the key string; older acceptors ignore the rest.
        if (RevisionEnabled && request.Data.PeekRemainingBytesSpan().SequenceEqual(CapabilityTrailer))
            peer.WireMode = PeerWireMode.Revision;
        return peer;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Stop();
        _listener.NetworkReceiveEvent -= OnNetworkReceive;
        _listener.PeerConnectedEvent -= OnPeerConnected;
        _listener.PeerDisconnectedEvent -= OnPeerDisconnected;
        _listener.ConnectionRequestEvent -= OnConnectionRequest;
        _listener.ClearNetworkReceiveEvent();
        _listener.ClearNetworkReceiveUnconnectedEvent();

        NetworkReceiveEvent = null;
        PeerConnectedEvent = null;
        PeerDisconnectedEvent = null;
        ConnectionRequestEvent = null;
        RevisionControlReceived = null;
    }
}
