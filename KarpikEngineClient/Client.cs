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
    private MouseEventSystem _eventSystem = new();

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

        
        // Создаем корневой элемент
        var root = new VisualElement("root");
        
        UI.Root = root;
        UI.Root.Style.BackgroundColor = new Color(255, 0, 0, 128);
        
        // Создаем таблицу стилей
        var styleSheet = CreateStyleSheet();
        root.StyleSheet.CopyFrom(styleSheet);
        
        // Создаем UI элементы
        CreateUI(root);
        
        UI.DefaultFont = Raylib.GetFontDefault();

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
        Raylib.InitWindow(800, 600, "Console Launcher");
        Raylib.SetWindowState(ConfigFlags.ResizableWindow);
        Raylib.SetWindowMinSize(400, 300);
        rlImGui.Setup();

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
        _eventSystem.Update(UI.Root);
        UI.Update();
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


        UI.Draw();
        Raylib.DrawText("Hello", 12, 12, 20, Color.Black);
        rlImGui.End();
        Raylib.EndDrawing();
    }

    private void Stop()
    {
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

static StyleSheet CreateStyleSheet()
    {
        var styleSheet = new StyleSheet();
        
        // Стили для панели
        var panelRule = new StyleRule(".panel");
        panelRule.Properties["width"] = "300px";
        panelRule.Properties["height"] = "400px";
        panelRule.Properties["background-color"] = "#f0f0f0";
        panelRule.Properties["border"] = "2px solid #ccc";
        panelRule.Properties["border-radius"] = "10px";
        panelRule.Properties["padding"] = "20px"; // Упрощенный padding
        panelRule.Properties["margin"] = "20px";  // Упрощенный margin
        panelRule.Properties["flex-direction"] = "column";
        panelRule.Properties["justify-content"] = "flex-start";
        panelRule.Properties["align-items"] = "stretch";
        
        styleSheet.AddRule(panelRule);
        
        // Стили для заголовка
        var headerRule = new StyleRule(".header");
        headerRule.Properties["height"] = "60px";
        headerRule.Properties["background-color"] = "#4CAF50";
        headerRule.Properties["margin-bottom"] = "15px";
        headerRule.Properties["border-radius"] = "5px";
        
        var headerHoverRule = new StyleRule(".header");
        headerHoverRule.Properties["background-color"] = "#45a049";
        headerRule.PseudoClasses[":hover"] = headerHoverRule;
        
        styleSheet.AddRule(headerRule);
        
        // Стили для кнопок
        var buttonRule = new StyleRule("button");
        buttonRule.Properties["height"] = "40px";
        buttonRule.Properties["background-color"] = "#2196F3";
        buttonRule.Properties["color"] = "white";
        buttonRule.Properties["border"] = "none";
        buttonRule.Properties["border-radius"] = "5px";
        buttonRule.Properties["margin-bottom"] = "10px";
        buttonRule.Properties["font-size"] = "16px";
        buttonRule.Properties["text-align"] = "center";
        
        var buttonHoverRule = new StyleRule("button");
        buttonHoverRule.Properties["background-color"] = "#1976D2";
        buttonRule.PseudoClasses[":hover"] = buttonHoverRule;
        
        var buttonActiveRule = new StyleRule("button");
        buttonActiveRule.Properties["background-color"] = "#0D47A1";
        buttonRule.PseudoClasses[":active"] = buttonActiveRule;
        
        styleSheet.AddRule(buttonRule);
        
        // Стили для контента
        var contentRule = new StyleRule(".content");
        contentRule.Properties["flex-grow"] = "1";
        contentRule.Properties["background-color"] = "white";
        contentRule.Properties["border"] = "1px solid #ddd";
        contentRule.Properties["border-radius"] = "5px";
        contentRule.Properties["padding"] = "15px";
        contentRule.Properties["margin-bottom"] = "15px";
        
        styleSheet.AddRule(contentRule);
        
        // Стили для футера
        var footerRule = new StyleRule(".footer");
        footerRule.Properties["height"] = "40px";
        footerRule.Properties["background-color"] = "#FF9800";
        footerRule.Properties["border-radius"] = "5px";
        footerRule.Properties["text-align"] = "center";
        footerRule.Properties["font-size"] = "14px";
        footerRule.Properties["color"] = "white";
        
        styleSheet.AddRule(footerRule);
        
        return styleSheet;
    }
    
    static void CreateUI(VisualElement root)
    {
        // Создаем основную панель
        var mainPanel = new VisualElement("main-panel");
        mainPanel.ClassList = "panel";
        // Не нужно устанавливать Position вручную - стили сделают это
        
        // Создаем заголовок
        var header = new VisualElement("header");
        header.ClassList = "header";
        
        // Создаем контент
        var content = new VisualElement("content");
        content.ClassList = "content";
        
        // Создаем кнопки
        var button1 = new Button("Primary Button");
        button1.Name = "btn-primary";
        
        var button2 = new Button("Secondary Button");
        button2.Name = "btn-secondary";
        button2.Style.BackgroundColor = new StyleValue<Color>(Color.Gray);
        
        var button3 = new Button("Danger Button");
        button3.Name = "btn-danger";
        button3.Style.BackgroundColor = new StyleValue<Color>(Color.Red);
        
        content.AddChild(button1);
        content.AddChild(button2);
        content.AddChild(button3);
        
        // Создаем футер
        var footer = new VisualElement("footer");
        footer.ClassList = "footer";
        
        // Добавляем манипуляторы
        var clickable1 = new Clickable();
        clickable1.OnClicked += () => Console.WriteLine("Primary button clicked!");
        button1.AddManipulator(clickable1);
        
        var clickable2 = new Clickable();
        clickable2.OnClicked += () => Console.WriteLine("Secondary button clicked!");
        button2.AddManipulator(clickable2);
        
        var clickable3 = new Clickable();
        clickable3.OnClicked += () => Console.WriteLine("Danger button clicked!");
        button3.AddManipulator(clickable3);
        
        // Собираем иерархию
        mainPanel.AddChild(header);
        mainPanel.AddChild(content);
        mainPanel.AddChild(footer);
        
        root.AddChild(mainPanel);
        
        // Создаем абсолютно позиционированный элемент
        var absoluteElement = new VisualElement("absolute-element");
        absoluteElement.Style.Width = new StyleValue<float>(100);
        absoluteElement.Style.Height = new StyleValue<float>(100);
        absoluteElement.Style.BackgroundColor = new StyleValue<Color>(Color.Purple);
        absoluteElement.Style.Position = new StyleValue<PositionType>(PositionType.Absolute);
        absoluteElement.Style.Right = new StyleValue<float>(20);
        absoluteElement.Style.Top = new StyleValue<float>(20);
        absoluteElement.Style.BorderRadius = new StyleValue<float>(50); // Круг
        
        root.AddChild(absoluteElement);
    }
}