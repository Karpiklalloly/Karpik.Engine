using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Game.Generated;
using Game.Generated.Client;
using Karpik.Engine.Client.UIToolkit;
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
    private NetManager _network;
    public static UIManager UIManager = null!;

    public void Run(in bool isRunning)
    {
        DateTime last = DateTime.Now;

        while (isRunning && !Raylib.WindowShouldClose())
        {
            var now = DateTime.Now;
            var deltaTime = (now - last).TotalSeconds;
            Time.Update(deltaTime);
            last = now;
            Update();
        }

        Stop();
    }

    public void Init()
    {
        NetworkManager.Instance.Register();

        var listener = new EventBasedNetListener();
        _network = new NetManager(listener);
        var port = GetFreePort();
        _network.Start(port);
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

        // Инициализируем окно сначала
        Raylib.InitWindow(800, 600, "Console Launcher");
        Raylib.SetWindowState(ConfigFlags.ResizableWindow);
        Raylib.SetWindowMinSize(400, 300);
        
        rlImGui.Setup();
        
        // Инициализируем новую UI систему
        UIManager = new UIManager();
        var root = new UIElement("root");
        root.AddChild(CreateDemoUI());
        UIManager.SetRoot(root);
        UIManager.Font = Raylib.GetFontDefault();
        var codes = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя"
                    + "0123456789"
                    + ".,!?-+()[]{}:;/\\\"'`~@#$%^&*=_|<> "
                    + "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
                    + "▼" + "▶";
        int count = 0;
        var chars = Raylib.LoadCodepoints(codes, ref count);
        var font = Raylib.LoadFontEx("Pressstart2p.ttf", 32, chars, count);
        Console.WriteLine((bool)Raylib.IsFontValid(font));
        UIManager.Font = font;
        //CreateNewUI();

        _builder = EcsPipeline.New()
            .Inject(Worlds.Instance.World)
            .Inject(Worlds.Instance.EventWorld)
            .Inject(Worlds.Instance.MetaWorld)
            .Inject(_modManager)
            .Inject(Camera.Main)
            .Inject(_network);

        InitEcs();

        _pipeline = _builder.Build();
        _pipeline.Init();
        Worlds.Instance.Init(_pipeline);

        Camera.Main.Position = new Vector3(10, 10, 10);
        Camera.Main.LookAt(Vector3.Zero);
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
        
        // Обновляем новую UI систему
        UIManager.Update(Time.DeltaTime);
        
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.DarkGray);

        rlImGui.Begin();
        Raylib.BeginMode3D(Camera.Main.CameraReference);
        _network.PollEvents();
        //Raylib.PollInputEvents();
        _pipeline.Run();
        _pipeline.GetRunner<EcsPausableRunner>().PausableRun();
        _pipeline.GetRunner<PausableLateRunner>().PausableLateRun();
        //TODO: Добавить прием ивента от сервака на фиксед апдейт

        Raylib.EndMode3D();
        
        // Рендерим новую UI систему
        UIManager.Render(Time.DeltaTime);

        rlImGui.End();
        Raylib.EndDrawing();
    }

    private void Stop()
    {
        // Очищаем UI ресурсы
        // UIManager.Root = null;
        
        Raylib.EnableCursor();
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

    public static int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private UIElement CreateDemoUI()
    {
        // 2. Создаем иерархию элементов с текстом
        var root = new UIElement("main");
        var headerCard = new UIElement("card") { Classes = { "card" }, Text = "Header Title" };
        var popupContainer = new UIElement { Classes = { "content-panel", "popup-container" } };
        var popup = new UIElement { Classes = { "popup" }, Text = "I am a popup!"};
        var hidden = new UIElement { Classes = { "hidden-element" } };
        
        root.AddChild(headerCard);
        root.AddChild(popupContainer);
        popupContainer.AddChild(popup); // Всплывающее окно - дочерний элемент
        popupContainer.AddChild(hidden); // Этот элемент не будет виден

        return root;
    }
}