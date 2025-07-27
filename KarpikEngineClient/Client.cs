using System.Numerics;
using Game.Generated;
using Game.Generated.Client;
using Karpik.Engine.Client.VisualElements;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.EcsRunners;
using Karpik.Engine.Shared.Modding;
using LiteNetLib;
using Network;
using Raylib_cs;
using rlImGui_cs;

namespace Karpik.Engine.Client;

public class Client
{
    private EcsPipeline _pipeline;
    private EcsPipeline.Builder _builder;
    private ModManager _modManager;
    private Camera2D _camera = new();
    private NetManager _network;
    private WorldEventListener[] _listeners;
    
    public void Run(in bool isRunning)
    {
        SpinWait wait = new SpinWait();
        while (isRunning && !Raylib.WindowShouldClose())
        {
            Update();
            wait.SpinOnce();
            //Thread.Sleep(1);
        }

        Stop();
    }

    public void Init()
    {
        NetworkManager.Instance.Register();
        
        var listener = new EventBasedNetListener();
        _network = new NetManager(listener);
        _network.Start(9050);
        _network.Connect("localhost", 9051, "MyGame");
        Rpc.Instance.Initialize(_network);
        listener.NetworkReceiveEvent += OnNetworkReceive;
        listener.PeerDisconnectedEvent += (peer, info) =>
        {
            Console.WriteLine("Disconnected from server. Clearing world...");
            NetworkManager.Instance.ClearClientCache();
        };

        Loader.Instance.Manager = new AssetManager();
        Loader.Instance.Manager.RegisterConverter<Texture2D>(fileName => Raylib.LoadTexture(fileName));
        Loader.Instance.Manager.RegisterConverter<ComponentsTemplate>(fileName =>
        {
            var json = Loader.Instance.Manager.ReadAllText(ApproveFileName(fileName, "json"));
            var options = new JsonSerializerSettings { Converters = { new ComponentArrayConverter() } };
            return JsonConvert.DeserializeObject<ComponentsTemplate>(json, options);
        });
        _modManager = new ModManager();
        _modManager.LoadMods("Mods");
        UI.Root = new VisualElement(new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()))
        {
            OffsetPosition = Vector2.Zero,
            Anchor = Anchor.TopLeft,
            Stretch = StretchMode.Both,
            Pivot = Vector2.Zero
        };
        UI.DefaultFont = Raylib.GetFontDefault();
        
        _listeners =
        [
            new WorldEventListener(Worlds.Instance.World),
            new WorldEventListener(Worlds.Instance.EventWorld),
            new WorldEventListener(Worlds.Instance.MetaWorld)
        ];
        
        _builder = EcsPipeline.New()
            .Inject(Worlds.Instance.World)
            .Inject(Worlds.Instance.EventWorld)
            .Inject(Worlds.Instance.MetaWorld)
            .Inject(_modManager)
            .Inject(_camera)
            .Inject(_network);
        
        InitEcs();
        
        _pipeline = _builder.BuildAndInit();
        Worlds.Instance.Init(_pipeline);
        Raylib.InitWindow(800, 600, "Console Launcher");
        Raylib.EnableCursor();
        rlImGui.Setup();
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        var packetType = (PacketType)reader.GetByte();
        if (packetType == PacketType.Snapshot)
        {
            NetworkManager.Instance.ApplySnapshot(Worlds.Instance.World, reader);
        }
        else if (packetType == PacketType.Command)
        {
            TargetClientRpcDispatcher.Instance.Dispatch(reader);
        }
        reader.Recycle();
    }

    public Client Add(IEcsModule module)
    {
        _builder.AddModule(module);
        return this;
    }
    
    private void Update()
    {
        Input.Update();
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.SkyBlue);
        
        rlImGui.Begin();
        _network.PollEvents();
        //Raylib.PollInputEvents();
        _pipeline.Run();
        _pipeline.GetRunner<EcsPausableRunner>().PausableRun();
        _pipeline.GetRunner<PausableLateRunner>().PausableLateRun();
        //TODO: Добавить прием ивента от сервака на фиксед апдейт
            
        Raylib.DrawText("Hello", 12, 12, 20, Color.Black);
        rlImGui.End();
        Raylib.EndDrawing();
    }
    
    private void Stop()
    {
        rlImGui.Shutdown();
        Raylib.CloseWindow();
        _network.Stop();
    }
    
    private void InitEcs()
    {
        _builder
            .AddRunner<EcsPausableRunner>()
            .AddRunner<PausableLateRunner>()
            
            .AddModule(new VisualModule())
            .AddModule(new InputModule())
            .AddModule(new TimeModule())
            .AddModule(new ModdingModule())
            .AddModule(new DemoModuleClient());
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