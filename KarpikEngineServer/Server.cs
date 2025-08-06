using Game.Generated;
using Game.Generated.Server;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Karpik.Engine.Shared.EcsRunners;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Server.DEMO;
using LiteNetLib;
using LiteNetLib.Utils;
using Network;

namespace Karpik.Engine.Server;

public class Server
{
    public const int TICKS_PER_SECOND = 20;
    public const int SLEEP_TIME = 1000 / TICKS_PER_SECOND;
    private readonly TimeSpan _tickInterval = TimeSpan.FromMilliseconds(SLEEP_TIME);
    private DateTime _nextTickTime;

    private EcsPipeline _pipeline;
    private EcsPipeline.Builder _builder;
    private NetManager _network;
    private ModManager _modManager;
    
    private WorldEventListener[] _listeners;
    private List<int> _destroyedEntities = [];
    private List<int> _newEntities = [];
    private int _nextNetworkId = 1;
    private List<int> _destroyedNetworkIds = [];
    private Dictionary<NetPeer, int> _peerToEntity = [];
    private Queue<(NetPeer, int)> _needSendLocalPlayer = [];
    
    private CommandDispatcher _commandDispatcher;
    
    public void Run(in bool isRunning)
    {
        Time.FixedDeltaTime = 1.0 / TICKS_PER_SECOND; 
        _nextTickTime = DateTime.Now;
        while (isRunning)
        {
            var now = DateTime.Now;
            if (now >= _nextTickTime)
            {
                Update();
                _nextTickTime = now + _tickInterval;
            }
        }

        Stop();
    }

    public void Init()
    {
        NetworkManager.Instance.Register();
        var listener = new EventBasedNetListener();
        _network = new NetManager(listener);
        _network.Start(9051);
        TargetClientRpcSender.Instance.Initialize(_network);
        listener.ConnectionRequestEvent += req => req.AcceptIfKey("MyGame");
        listener.PeerConnectedEvent += peer =>
        {
            Console.WriteLine($"Player connected: {peer.Id}");
            var world = Worlds.Instance.World;
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
        listener.NetworkReceiveEvent += OnNetworkReceive;
        _commandDispatcher = new CommandDispatcher();
        Loader.Instance.Manager = new AssetManager();
        Loader.Instance.Manager.RegisterConverter<ComponentsTemplate>(fileName =>
        {
            var json = Loader.Instance.Manager.ReadAllText(ApproveFileName(fileName, "json"));
            var options = new JsonSerializerSettings { Converters = { new ComponentArrayConverter() } };
            return JsonConvert.DeserializeObject<ComponentsTemplate>(json, options);
        });
        
        _modManager = new ModManager();
        _modManager.LoadMods("Mods");
        
        _listeners =
        [
            new WorldEventListener(Worlds.Instance.World),
            new WorldEventListener(Worlds.Instance.EventWorld),
            new WorldEventListener(Worlds.Instance.MetaWorld)
        ];
        _listeners[0].RegisterDel(e => _destroyedEntities.Add(e));
        _listeners[0].RegisterNew(e => _newEntities.Add(e));
        
        _builder = EcsPipeline.New()
            .Inject(Worlds.Instance.World)
            .Inject(Worlds.Instance.EventWorld)
            .Inject(Worlds.Instance.MetaWorld)
            .Inject(_modManager)
            .Inject(_network);
        
        InitEcs();
        
        _pipeline = _builder.BuildAndInit();

        Worlds.Instance.Init(_pipeline);
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("Initializing server...");

        for (int i = 0; i < 100; i++)
        {
            var entity =  Worlds.Instance.World.NewEntityLong();
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
        SendSnapshotToAll();
        if (_needSendLocalPlayer.Count > 0)
        {
            var (peer, netID) = _needSendLocalPlayer.Dequeue();
            TargetClientRpcSender.Instance.SetLocalPlayer(peer, new SetLocalPlayerTargetRpc()
            {
                LocalPlayerNetId = netID,
            });
        }
    }

    private void Stop()
    {
        _network.Stop();
        Console.WriteLine("Stopping server...");
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        if (reader.AvailableBytes == 0) { reader.Recycle(); return; }
        var packetType = (PacketType)reader.GetByte();
        if (packetType == PacketType.Command)
        {
            int player = _peerToEntity[peer];
            var playerEntity = Worlds.Instance.World.GetEntityLong(player);
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
        NetworkManager.Instance.WriteSnapshot(Worlds.Instance.World, writer, _destroyedNetworkIds);
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