using System.Drawing;
using System.Numerics;
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
    
    private WorldEventListener[] _listeners = null!;
    private List<int> _destroyedEntities = [];
    private List<int> _newEntities = [];
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
            
            // Add network ID
            world.GetPool<NetworkId>().Add(player).Id = _networkIdGenerator.Next();
            
            // Add basic components
            world.GetPool<Health>().Add(player).Value = 1;
            world.GetPool<Player>().Add(player);
            world.GetPool<SpriteData>().Add(player) = new SpriteData()
            {
                Color = Color.White,
                TexturePath = "Sprites/Player.png",
                Size = new Vector2(1, 2)
            };
            
            // Get spawn position from first spawn point
            Vector2 spawnPosition = GetSpawnPosition();
            
            // Add physics - Transform2D
            ref var transform = ref world.GetPool<Transform2D>().Add(player);
            transform.Position = spawnPosition;
            transform.Rotation = 0;
            
            // Add physics - Velocity2D
            ref var velocity = ref world.GetPool<Velocity2D>().Add(player);
            velocity.Linear = Vector2.Zero;
            velocity.Angular = 0;
            
            // Add physics - CreateBodyRequest (kinematic player body for platformer controller)
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
                JumpForce = 300.0f,
                Gravity = 30.0f,
                MaxFallSpeed = 25.0f,
                IsGrounded = false,
                LastJumpTime = 0,
                JumpCooldown = 0.2f
            };
            
            _peerToEntity.Add(peer, player);
            Console.WriteLine(world.GetPool<NetworkId>().Get(player).Id);
            _needSendLocalPlayer.Enqueue((peer, world.GetPool<NetworkId>().Get(player).Id));
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

    public void Update()
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
        req.AcceptIfKey(_config.Key);
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
