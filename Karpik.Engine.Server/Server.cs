using Game.Generated;
using Game.Generated.Server;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Server.DEMO;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.DependencyInjection;
using Network;

namespace Karpik.Engine.Server;

public class Server
{
    public const int TICKS_PER_SECOND = 20;
    public const int SLEEP_TIME = 1000 / TICKS_PER_SECOND;
    private readonly TimeSpan _tickInterval = TimeSpan.FromMilliseconds(SLEEP_TIME);
    private DateTime _nextTickTime;

    private EcsDefaultWorld _world = new();
    private EcsEventWorld _eventWorld = new();
    private EcsMetaWorld _metaWorld = new();
    private EcsPipeline _pipeline;
    private EcsPipeline.Builder _builder;
    private EcsRunParallelRunner _parallelRunner;
    
    private NetManager _network;
    private ModManager _modManager = new();
    private AssetsManager _assetsManager = new();
    private Tween _tween = new();
    
    private WorldEventListener[] _listeners;
    private List<int> _destroyedEntities = [];
    private List<int> _newEntities = [];
    private int _nextNetworkId = 1;
    private List<int> _destroyedNetworkIds = [];
    private Dictionary<NetPeer, int> _peerToEntity = [];
    private Queue<(NetPeer, int)> _needSendLocalPlayer = [];
    
    private CommandDispatcher _commandDispatcher = new();

    private NetworkManager _networkManager = new();
    private TargetClientRpcSender _rpcSender = new();
    private ServiceProvider _serviceProvider;

    public void Run(in bool isRunning)
    {
        Time.FixedDeltaTime = 1.0 / TICKS_PER_SECOND; 
        _nextTickTime = DateTime.Now;
        while (isRunning)
        {
            var now = DateTime.Now;
            if (now >= _nextTickTime)
            {
                Time.Update(Time.FixedDeltaTime);
                Update();
                _nextTickTime = now + _tickInterval;
            }
        }

        Stop();
    }

    public void Init()
    {
        var listener = new EventBasedNetListener();
        _network = new NetManager(listener);
        _network.Start(9051);
        listener.ConnectionRequestEvent += static req => req.AcceptIfKey("MyGame");
        listener.NetworkReceiveEvent += OnNetworkReceive;
        listener.PeerConnectedEvent += peer =>
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
        
        var services = new ServiceCollection();
        services
            .AddSingleton(_world)
            .AddSingleton(_eventWorld)
            .AddSingleton(_metaWorld)
            .AddSingleton(_network)
            .AddSingleton(_assetsManager)
            .AddSingleton(_commandDispatcher)
            .AddSingleton(_networkManager)
            .AddSingleton(_rpcSender)
            .AddSingleton(_modManager)
            .AddSingleton(_tween);
        _serviceProvider = services.BuildServiceProvider();
        
        _serviceProvider.Inject(_commandDispatcher);
        _serviceProvider.Inject(_networkManager);
        _serviceProvider.Inject(_rpcSender);
        _serviceProvider.Inject(_modManager);
        _serviceProvider.Inject(_assetsManager);
        
        _modManager.Init(ModManager.Type.Server);
        _modManager.LoadMods(_assetsManager.ModsPath);
        
        _listeners =
        [
            new WorldEventListener(_world),
            new WorldEventListener(_eventWorld),
            new WorldEventListener(_metaWorld)
        ];
        _listeners[0].RegisterDel(e => _destroyedEntities.Add(e));
        _listeners[0].RegisterNew(e => _newEntities.Add(e));
        
        BaseSystem.InitWorlds(_world, _eventWorld, _metaWorld);
        _builder = EcsPipeline.New(_serviceProvider)
            .Inject(_world)
            .Inject(_eventWorld)
            .Inject(_metaWorld)
            .Inject(_modManager)
            .Inject(_network)
            .Inject(_assetsManager)
            .Inject(_tween);
        
        InitEcs();
        
        _pipeline = _builder.BuildAndInit();
        
        _parallelRunner = _pipeline.GetRunner<EcsRunParallelRunner>();
        _parallelRunner.Init();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("Initializing server...");

        for (int i = 0; i < 100; i++)
        {
            var entity =  _world.NewEntityLong();
            entity.Add<Health>();
        }
    }

    public Server Add(IEcsModule module)
    {
        _builder.AddModule(module);
        return this;
    }

    private void Update()
    {
        _network.PollEvents();
        _pipeline.Run();
        _parallelRunner.RunParallel();
        BaseSystem.RunBuffers();
        SendSnapshotToAll();
        if (_needSendLocalPlayer.Count > 0)
        {
            var (peer, netID) = _needSendLocalPlayer.Dequeue();
            _rpcSender.SetLocalPlayer(peer, new SetLocalPlayerTargetRpc()
            {
                LocalPlayerNetId = netID,
            });
        }
    }

    private void Stop()
    {
        _network.Stop();
        _serviceProvider.Dispose();
        Console.WriteLine("Stopping server...");
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        if (reader.AvailableBytes == 0) { reader.Recycle(); return; }
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
    
    private void SendSnapshotToAll()
    {
        var writer = new NetDataWriter();
        writer.Put((byte)PacketType.Snapshot);
        _networkManager.WriteSnapshot(_world, writer, _destroyedNetworkIds);
        _network.SendToAll(writer, DeliveryMethod.Unreliable);

        _destroyedNetworkIds.Clear();
        _newEntities.Clear();
        _destroyedEntities.Clear();
    }
    
    private void InitEcs()
    {
        _builder
            .AddRunner<EcsPausableRunner>()
            .AddRunner<PausableLateRunner>()
            .AddRunner<EcsRunParallelRunner>()
            .AddModule(new DemoModule(_destroyedNetworkIds))
            .AddModule(new TimeModule())
            .AddModule(new ModdingModule());
    }
    
    private static string ApproveFileName(string path, string extension)
    {
        extension = $".{extension}";
        if (path[^extension.Length..] != extension)
        {
            return path + extension;
        }
        return path;
    }
}