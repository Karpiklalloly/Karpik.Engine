using System.Drawing;
using System.Numerics;
using System.Security.Cryptography;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Server.Extensions;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Log;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib.Configs;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.MyGame.Server.Main.Systems;

internal class NetworkSystem : ISystemInit, ISystemUpdate, ISystemDestroy
{
    private const float DisconnectedPlayerTtlSeconds = 60.0f;

    private class PlayerSessionAspect : EcsAspect
    {
        public EcsReadonlyPool<PlayerSession> Session = Inc;
    }

    private class PlayerAspect : EcsAspect
    {
        public EcsReadonlyPool<Player> Player = Inc;
    }

    private class NetworkIdAspect : EcsAspect
    {
        public EcsReadonlyPool<NetworkId> NetworkId = Inc;
    }

    private class DisconnectCleanupAspect : EcsAspect
    {
        public EcsPool<PlayerConnection> Connection = Inc;
        public EcsPool<PlayerDisconnectCleanup> Cleanup = Inc;
    }

    [DI] private INetworkManager _networkManager = null!;
    [DI] private TargetRpcSender _rpc = null!;
    [DI] private ILogger _logger = null!;
    [DI] private NetworkConfig _config = null!;
    
    [DI] private EcsDefaultWorld _world = null!;
    [DI] private EcsEventWorld _eventWorld = null!;
    [DI] private EcsMetaWorld _metaWorld = null!;
    [DI] private NetworkManager _network = null!;
    [DI] private CommandDispatcher _commandDispatcher = null!;
    [DI] private NetworkIdGenerator _networkIdGenerator = null!;
    [DI] private Time _time = null!;
    
    private WorldEventListener[] _listeners = null!;
    private List<int> _destroyedEntities = [];
    private List<int> _newEntities = [];
    private List<int> _destroyedNetworkIds = [];
    private List<int> _playersToDestroy = [];
    private Dictionary<int, int> _entityToNetworkId = [];
    private Dictionary<IPeer, int> _peerToEntity = [];
    private Queue<(IPeer, int, long)> _needSendLocalPlayer = [];

    private INetworkManager.PeerConnectionEventHandler _onPeerConnected = null!;
    private INetworkManager.PeerDisconnectionEventHandler _onPeerDisconnected = null!;
    
    public void Init()
    {
        _network.Initialize();
        _networkIdGenerator.EnsureAtLeast(GetMaxNetworkId());
        ResetRuntimeConnections();
        RebuildNetworkIdCache();
        _networkManager.ConnectionRequestEvent += OnConnectionRequest;
        _networkManager.NetworkReceiveEvent += OnNetworkReceive;
        _onPeerConnected = peer =>
        {
            Console.WriteLine($"Player connected: {peer.Id}");
        };
        _onPeerDisconnected = (peer, _) =>
        {
            if (_peerToEntity.Remove(peer, out var entity))
            {
                MarkDisconnected(entity);
            }
        };
        _networkManager.PeerConnectedEvent += _onPeerConnected;
        _networkManager.PeerDisconnectedEvent += _onPeerDisconnected;
        
        _listeners =
        [
            new WorldEventListener(_world),
            new WorldEventListener(_eventWorld),
            new WorldEventListener(_metaWorld)
        ];
        _listeners[0].OnNewEntityDeleted += OnDelEntity;
        _listeners[0].OnNewEntityCreated += OnNewEntity;
    }
    
    private Vector2 GetSpawnPosition()
    {
        var mask = EcsStaticMask.Inc<RespawnPoint>().Inc<Transform2D>().Build();
        var span = _world.Where(mask);
        
        // Default spawn position - LevelInitSystem will create platforms but we'll use a safe default
        // If LevelInitSystem creates RespawnPoint entities, we could query them here
        // For now, spawn at a position above the ground platform (y=-5 is ground level)
        return _world.GetPool<Transform2D>().Get(span[0]).Position;
    }
    
    private void OnNetworkReceive(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        try
        {
            if (reader.AvailableBytes == 0)
            {
                return;
            }

            var packetType = (PacketType)reader.GetByte();
            if (packetType == PacketType.Handshake)
            {
                AttachOrCreatePlayer(peer, reader.GetLong());
            }
            else if (packetType == PacketType.Command)
            {
                if (!_peerToEntity.TryGetValue(peer, out int player))
                {
                    return;
                }

                var playerEntity = _world.GetEntityLong(player);
                if (playerEntity.IsAlive)
                {
                    _commandDispatcher.Dispatch(playerEntity.ID, reader);
                }
            }
        }
        finally
        {
            reader.Recycle();
        }
    }

    public void Update()
    {
        CleanupDisconnectedPlayers();
        SendSnapshotToAll();
        if (_needSendLocalPlayer.Count > 0)
        {
            var (peer, netID, reconnectToken) = _needSendLocalPlayer.Dequeue();
            _rpc.SetLocalPlayer(peer, new SetLocalPlayerTargetRpc()
            {
                LocalPlayerNetId = netID,
                ReconnectToken = reconnectToken,
            });
        }
    }
    
    private void SendSnapshotToAll()
    {
        var writer = _networkManager.CreateWriter();
        writer.Put((byte)PacketType.Snapshot);
        UpdateNetworkIdCache();
        _network.WriteSnapshot(_world, writer, _destroyedNetworkIds);
        _networkManager.SendToAll(writer, DeliveryMethod.Unreliable);

        _destroyedNetworkIds.Clear();
        _newEntities.Clear();
        _destroyedEntities.Clear();
    }

    private void OnDelEntity(int e)
    {
        _destroyedEntities.Add(e);
        QueueDestroyedNetworkId(e);
    }
    
    private void OnNewEntity(int e)
    {
        _newEntities.Add(e);
    }

    private void OnConnectionRequest(IConnectionRequest req)
    {
        req.AcceptIfKey(_config.Key);
    }

    public void Destroy()
    {
        _networkManager.ConnectionRequestEvent -= OnConnectionRequest;
        _networkManager.NetworkReceiveEvent -= OnNetworkReceive;
        _networkManager.PeerConnectedEvent -= _onPeerConnected;
        _networkManager.PeerDisconnectedEvent -= _onPeerDisconnected;
        _onPeerConnected = null!;
        _onPeerDisconnected = null!;
        
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

        _playersToDestroy.Clear();
        _playersToDestroy = null!;

        _entityToNetworkId.Clear();
        _entityToNetworkId = null!;
        
        _networkManager.Stop();
    }

    private void AttachOrCreatePlayer(IPeer peer, long reconnectToken)
    {
        Console.WriteLine($"Handshake from peer {peer.Id}, reconnect token {reconnectToken}");

        if (_peerToEntity.TryGetValue(peer, out var previousEntity))
        {
            var previousPlayer = _world.GetEntityLong(previousEntity);
            if (previousPlayer.IsAlive)
            {
                var previousToken = GetReconnectToken(previousEntity);
                if (reconnectToken == 0 || previousToken == reconnectToken)
                {
                    SendLocalPlayer(peer, previousEntity, previousToken);
                    return;
                }

                MarkDisconnected(previousEntity);
            }

            _peerToEntity.Remove(peer);
        }

        var player = FindPlayerByReconnectToken(reconnectToken);
        if (!player.IsAlive)
        {
            reconnectToken = GenerateReconnectToken();
            player = CreatePlayer(reconnectToken);
            Console.WriteLine($"Created player {player.ID} for peer {peer.Id}, reconnect token {reconnectToken}");
        }
        else
        {
            Console.WriteLine($"Reattached peer {peer.Id} to existing player {player.ID}");
        }

        RemoveExistingPeerBindings(player.ID);
        _peerToEntity[peer] = player.ID;
        MarkConnected(player.ID, peer.Id);

        SendLocalPlayer(peer, player.ID, reconnectToken);
    }

    private void SendLocalPlayer(IPeer peer, int player, long reconnectToken)
    {
        var networkId = _world.GetPool<NetworkId>().Get(player).Id;
        _needSendLocalPlayer.Enqueue((peer, networkId, reconnectToken));
        Console.WriteLine($"Attached peer {peer.Id} to player network id {networkId}, reconnect token {reconnectToken}");
    }

    private entlong CreatePlayer(long reconnectToken)
    {
        var world = _world;
        var player = world.NewEntity();

        var networkId = _networkIdGenerator.Next();
        world.GetPool<NetworkId>().Add(player).Id = networkId;
        CacheNetworkId(player, networkId);
        world.GetPool<PlayerSession>().Add(player).ReconnectToken = reconnectToken;
        world.GetPool<Health>().Add(player).Value = 1;
        world.GetPool<Player>().Add(player);
        world.GetPool<SpriteData>().Add(player) = new SpriteData
        {
            Color = Color.White,
            TexturePath = "Sprites/Player.png",
            Size = new Vector2(1, 2)
        };

        Vector2 spawnPosition = GetSpawnPosition();

        ref var transform = ref world.GetPool<Transform2D>().Add(player);
        transform.Position = spawnPosition;
        transform.Rotation = 0;

        ref var velocity = ref world.GetPool<Velocity2D>().Add(player);
        velocity.Linear = Vector2.Zero;
        velocity.Angular = 0;

        ref var bodyRequest = ref world.GetPool<CreateBodyRequest>().Add(player);
        bodyRequest.BodyConfig = new BodyConfig
        {
            Type = BodyType.Kinematic,
            Mass = 1.0f,
            Friction = 0.0f,
            Restitution = 0.0f,
            IsSensor = false,
            IgnoreGravity = true,
            CategoryBits = Physics2DLayers.Player,
            MaskBits = Physics2DLayers.Player | Physics2DLayers.Platform
        };
        bodyRequest.ShapeConfig = ShapeConfig.Box(new Vector2(1, 2));

        world.GetPool<KinematicCharacterController>().Add(player) = new KinematicCharacterController
        {
            MoveSpeed = 8.0f,
            JumpSpeed = 9.0f,
            Gravity = -9.8f,
            MaxFallSpeed = 25.0f,
            IsGrounded = false,
            LastJumpTime = -0.2f,
            JumpCooldown = 0.2f,
            MaxGroundAngle = 45 * MathF.PI / 180
        };

        return _world.GetEntityLong(player);
    }

    private entlong FindPlayerByReconnectToken(long reconnectToken)
    {
        if (reconnectToken == 0)
        {
            return entlong.NULL;
        }

        foreach (var entity in _world.Where(out PlayerSessionAspect aspect))
        {
            if (aspect.Session.Get(entity).ReconnectToken != reconnectToken)
            {
                continue;
            }

            return _world.GetEntityLong(entity);
        }

        return entlong.NULL;
    }

    private long GetReconnectToken(int entity)
    {
        var pool = _world.GetPool<PlayerSession>();
        return pool.Has(entity)
            ? pool.Get(entity).ReconnectToken
            : 0;
    }

    private long GenerateReconnectToken()
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        long token;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            token = BitConverter.ToInt64(bytes) & long.MaxValue;
        } while (token == 0 || ReconnectTokenExists(token));

        return token;
    }

    private bool ReconnectTokenExists(long reconnectToken)
    {
        foreach (var entity in _world.Where(out PlayerSessionAspect aspect))
        {
            if (aspect.Session.Get(entity).ReconnectToken == reconnectToken)
            {
                return true;
            }
        }

        return false;
    }

    private int GetMaxNetworkId()
    {
        var maxNetworkId = 0;
        foreach (var entity in _world.Where(out NetworkIdAspect aspect))
        {
            var networkId = aspect.NetworkId.Get(entity).Id;
            if (networkId > maxNetworkId)
            {
                maxNetworkId = networkId;
            }
        }

        return maxNetworkId;
    }

    private void MarkConnected(int entity, int peerId)
    {
        var pool = _world.GetPool<PlayerConnection>();
        if (pool.Has(entity))
        {
            ref var connection = ref pool.Get(entity);
            connection.PeerId = peerId;
            connection.Connected = true;
            _world.GetPool<PlayerDisconnectCleanup>().TryDel(entity);
            return;
        }

        pool.Add(entity) = new PlayerConnection
        {
            PeerId = peerId,
            Connected = true
        };
        _world.GetPool<PlayerDisconnectCleanup>().TryDel(entity);
    }

    private void MarkDisconnected(int entity)
    {
        var pool = _world.GetPool<PlayerConnection>();
        if (!pool.Has(entity))
        {
            return;
        }

        ref var connection = ref pool.Get(entity);
        connection.PeerId = -1;
        connection.Connected = false;

        var cleanupPool = _world.GetPool<PlayerDisconnectCleanup>();
        if (cleanupPool.Has(entity))
        {
            cleanupPool.Get(entity).RemainingSeconds = DisconnectedPlayerTtlSeconds;
        }
        else
        {
            cleanupPool.Add(entity).RemainingSeconds = DisconnectedPlayerTtlSeconds;
        }
    }

    private void ResetRuntimeConnections()
    {
        var connectionPool = _world.GetPool<PlayerConnection>();
        var cleanupPool = _world.GetPool<PlayerDisconnectCleanup>();

        foreach (var entity in _world.Where(out PlayerAspect aspect))
        {
            if (connectionPool.Has(entity))
            {
                ref var connection = ref connectionPool.Get(entity);
                connection.PeerId = -1;
                connection.Connected = false;
            }
            else
            {
                connectionPool.Add(entity) = new PlayerConnection
                {
                    PeerId = -1,
                    Connected = false
                };
            }

            if (cleanupPool.Has(entity))
            {
                cleanupPool.Get(entity).RemainingSeconds = DisconnectedPlayerTtlSeconds;
            }
            else
            {
                cleanupPool.Add(entity).RemainingSeconds = DisconnectedPlayerTtlSeconds;
            }
        }
    }

    private void RemoveExistingPeerBindings(int entity)
    {
        foreach (var pair in _peerToEntity.ToArray())
        {
            if (pair.Value == entity)
            {
                _peerToEntity.Remove(pair.Key);
            }
        }
    }

    private void CleanupDisconnectedPlayers()
    {
        _playersToDestroy.Clear();

        foreach (var entity in _world.Where(out DisconnectCleanupAspect aspect))
        {
            ref var connection = ref aspect.Connection.Get(entity);
            if (connection.Connected)
            {
                aspect.Cleanup.Del(entity);
                continue;
            }

            ref var cleanup = ref aspect.Cleanup.Get(entity);
            cleanup.RemainingSeconds -= (float)_time.DeltaTime;
            if (cleanup.RemainingSeconds <= 0.0f)
            {
                _playersToDestroy.Add(entity);
            }
        }

        foreach (var entity in _playersToDestroy)
        {
            var player = _world.GetEntityLong(entity);
            if (!player.IsAlive)
            {
                continue;
            }

            RemoveExistingPeerBindings(entity);
            QueueDestroyedNetworkId(entity);
            Console.WriteLine($"Removing disconnected player entity {entity}");
            _world.DelEntity(entity);
        }
    }

    private void QueueDestroyedNetworkId(int entity)
    {
        if (!_entityToNetworkId.Remove(entity, out var networkId))
        {
            return;
        }

        if (!_destroyedNetworkIds.Contains(networkId))
        {
            _destroyedNetworkIds.Add(networkId);
        }
    }

    private void RebuildNetworkIdCache()
    {
        _entityToNetworkId.Clear();
        UpdateNetworkIdCache();
    }

    private void UpdateNetworkIdCache()
    {
        foreach (var entity in _world.Where(out NetworkIdAspect aspect))
        {
            _entityToNetworkId[entity] = aspect.NetworkId.Get(entity).Id;
        }
    }

    private void CacheNetworkId(int entity, int networkId)
    {
        _entityToNetworkId[entity] = networkId;
    }
}

public struct PlayerDisconnectCleanup : IEcsComponent
{
    public float RemainingSeconds;
}
