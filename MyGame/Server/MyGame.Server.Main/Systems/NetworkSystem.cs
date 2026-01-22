using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Server.Extensions;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Log;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

internal class NetworkSystem : IEcsInit, IEcsRun, IEcsDestroy
{
    [DI] private INetworkManager _networkManager = null!;
    [DI] private TargetRpcSender _rpc = null!;
    [DI] private ILogger _logger = null!;
    
    [DI] private EcsDefaultWorld _world = null!;
    [DI] private EcsEventWorld _eventWorld = null!;
    [DI] private EcsMetaWorld _metaWorld = null!;
    [DI] private NetworkManager _network = null!;
    [DI] private CommandDispatcher _commandDispatcher = null!;
    
    private WorldEventListener[] _listeners = null!;
    private List<int> _destroyedEntities = [];
    private List<int> _newEntities = [];
    private int _nextNetworkId = 1;
    private List<int> _destroyedNetworkIds = [];
    private Dictionary<IPeer, int> _peerToEntity = [];
    private Queue<(IPeer, int)> _needSendLocalPlayer = [];

    private INetworkManager.PeerConnectionEventHandler _onPeerConnected = null!;
    
    public void Init()
    {
        _network.Initialize();
        _networkManager.ConnectionRequestEvent += OnConnectionRequest;
        _networkManager.NetworkReceiveEvent += OnNetworkReceive;
        _onPeerConnected = peer =>
        {
            Console.WriteLine($"Player connected: {peer.Id}");
            var world = _world;
            var player = world.NewEntity();
            world.GetPool<NetworkId>().Add(player).Id = _nextNetworkId++;
            world.GetPool<Position>().Add(player) = new Position()
            {
                X = 0,
                Y = 0
            };
            world.GetPool<Health>().Add(player).Value = 1;
            world.GetPool<Player>().Add(player);
            _peerToEntity.Add(peer, player);
            _needSendLocalPlayer.Enqueue((peer, _nextNetworkId - 1));
        };
        _networkManager.PeerConnectedEvent += _onPeerConnected;
        
        _listeners =
        [
            new WorldEventListener(_world),
            new WorldEventListener(_eventWorld),
            new WorldEventListener(_metaWorld)
        ];
        _listeners[0].OnNewEntityDeleted += OnDelEntity;
        _listeners[0].OnNewEntityCreated += OnNewEntity;
    }
    
    private void OnNetworkReceive(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        if (reader.AvailableBytes == 0)
        {
            reader.Recycle();
            return;
        }
        
        var packetType = (PacketType)reader.GetByte();
        if (packetType == PacketType.Command)
        {
            int player = _peerToEntity[peer];
            var playerEntity = _world.GetEntityLong(player);
            if (playerEntity.IsAlive)
            {
                _commandDispatcher.Dispatch(playerEntity.ID, reader);
            }
        }
        reader.Recycle();
    }

    public void Run()
    {
        SendSnapshotToAll();
        if (_needSendLocalPlayer.Count > 0)
        {
            var (peer, netID) = _needSendLocalPlayer.Dequeue();
            _rpc.SetLocalPlayer(peer, new SetLocalPlayerTargetRpc()
            {
                LocalPlayerNetId = netID,
            });
        }
    }
    
    private void SendSnapshotToAll()
    {
        var writer = _networkManager.CreateWriter();
        writer.Put((byte)PacketType.Snapshot);
        _network.WriteSnapshot(_world, writer, _destroyedNetworkIds);
        _networkManager.SendToAll(writer, DeliveryMethod.Unreliable);

        _destroyedNetworkIds.Clear();
        _newEntities.Clear();
        _destroyedEntities.Clear();
    }

    private void OnDelEntity(int e)
    {
        _destroyedEntities.Add(e);
    }
    
    private void OnNewEntity(int e)
    {
        _newEntities.Add(e);
    }

    private void OnConnectionRequest(IConnectionRequest req)
    {
        req.AcceptIfKey("MyGame");
    }

    public void Destroy()
    {
        _networkManager.ConnectionRequestEvent -= OnConnectionRequest;
        _networkManager.NetworkReceiveEvent -= OnNetworkReceive;
        _networkManager.PeerConnectedEvent -= _onPeerConnected;
        _onPeerConnected = null!;
        
        _listeners[0].OnNewEntityDeleted -= OnDelEntity;
        _listeners[0].OnNewEntityCreated -= OnNewEntity;
        
        foreach (var listener in _listeners)
        {
            listener.Dispose();
        }
        
        _destroyedEntities.Clear();
        _destroyedEntities = null!;
        
        _newEntities.Clear();
        _newEntities = null!;
        
        _peerToEntity.Clear();
        _peerToEntity = null!;
        
        _needSendLocalPlayer.Clear();
        _needSendLocalPlayer = null!;
        
        _destroyedNetworkIds.Clear();
        _destroyedNetworkIds = null!;
        
        _networkManager.Stop();
    }
}