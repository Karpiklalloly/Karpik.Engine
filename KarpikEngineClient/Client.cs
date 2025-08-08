using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Game.Generated;
using Game.Generated.Client;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Client.UIToolkit.Core;
using Karpik.Engine.Client.UIToolkit.Elements;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.EcsRunners;
using Karpik.Engine.Shared.Modding;
using LiteNetLib;
using Network;
using Raylib_cs;
using rlImGui_cs;
using Position = Karpik.Engine.Client.UIToolkit.Core.Position;

namespace Karpik.Engine.Client;

public class Client
{
    private EcsPipeline _pipeline;
    private EcsPipeline.Builder _builder;
    private ModManager _modManager;
    private NetManager _network;
    private UIManager _uiManager = null!;

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
        _uiManager = new UIManager();
        CreateNewUI();

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
        _uiManager.Update((float)Time.DeltaTime);
        
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.SkyBlue);

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
        _uiManager.Render();
        
        // Простая отладочная информация
        if (_uiManager.Root != null)
        {
            Raylib.DrawText($"New UI System - Root: {_uiManager.Root.Size.X}x{_uiManager.Root.Size.Y}", 12, 12, 16, Color.Black);
            Raylib.DrawText($"Children: {_uiManager.Root.Children.Count}", 12, 32, 16, Color.Black);
        }
        rlImGui.End();
        Raylib.EndDrawing();
    }

    private void Stop()
    {
        // Очищаем UI ресурсы
        _uiManager.Root = null;
        
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

    private void CreateNewUI()
    {
        // Создаем корневой элемент
        var root = new VisualElement("root");
        
        // Создаем главную панель
        var mainPanel = new Panel("main-panel");
        mainPanel.Style.Width = 350;
        mainPanel.Style.Height = 450;
        mainPanel.Style.Margin = new Margin(20);
        
        // Создаем заголовок
        var header = new VisualElement("header");
        header.AddClass("header");
        
        // Создаем область контента
        var content = new VisualElement("content");
        content.AddClass("content");
        
        // Создаем кнопки
        var button1 = new Button("Primary Button");
        var clickable1 = new ClickableManipulator();
        clickable1.OnClicked += () => Console.WriteLine("Primary button clicked!");
        button1.AddManipulator(clickable1);
        
        var button2 = new Button("Secondary Button");
        button2.Style.BackgroundColor = Color.Gray;
        var clickable2 = new ClickableManipulator();
        clickable2.OnClicked += () => Console.WriteLine("Secondary button clicked!");
        button2.AddManipulator(clickable2);
        
        var button3 = new Button("Danger Button");
        button3.Style.BackgroundColor = Color.Red;
        var clickable3 = new ClickableManipulator();
        clickable3.OnClicked += () => Console.WriteLine("Danger button clicked!");
        button3.AddManipulator(clickable3);
        
        // Собираем иерархию
        content.AddChild(button1);
        content.AddChild(button2);
        content.AddChild(button3);
        
        mainPanel.AddChild(header);
        mainPanel.AddChild(content);
        
        root.AddChild(mainPanel);
        
        // Создаем абсолютно позиционированный элемент
        var absoluteElement = new VisualElement("absolute-element");
        absoluteElement.Style.Width = 100;
        absoluteElement.Style.Height = 100;
        absoluteElement.Style.BackgroundColor = Color.Purple;
        absoluteElement.Style.Position = Position.Absolute;
        absoluteElement.Style.Right = 20;
        absoluteElement.Style.Top = 20;
        absoluteElement.Style.BorderRadius = 50; // Круг
        
        root.AddChild(absoluteElement);
        
        _uiManager.SetRoot(root);
    }
}