using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Game.Generated;
using Game.Generated.Client;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.Modding;
using LiteNetLib;
using Microsoft.Extensions.DependencyInjection;
using Network;
using Raylib_cs;
using rlImGui_cs;

namespace Karpik.Engine.Client;

public class Client
{
    private EcsDefaultWorld _world = new();
    private EcsEventWorld _eventWorld = new();
    private EcsMetaWorld _metaWorld = new();
    private EcsPipeline _pipeline;
    private EcsPipeline.Builder _builder;
    private EcsRunParallelRunner _parallelRunner;
    
    private UIManager _uiManager = new();
    private AssetsManager _assetsManager = new();
    private Tween _tween = new();
    private Input _input = new();
    private ModManager _modManager = new();
    private NetManager _network;
    private Drawer _drawer = new();

    private NetworkManager _networkManager = new();
    private Rpc _rpc = new();
    private TargetClientRpcDispatcher _targetClientRpcDispatcher = new();
    private ServiceProvider _serviceProvider;

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
        var listener = new EventBasedNetListener();
        _network = new NetManager(listener);
        var port = GetFreePort();
        _network.Start(port);
        _network.Connect("localhost", 9051, "MyGame");
        listener.NetworkReceiveEvent += OnNetworkReceive;
        listener.PeerDisconnectedEvent += (peer, info) =>
        {
            Console.WriteLine("Disconnected from server. Clearing world...");
            _networkManager.ClearClientCache();
        };

        var services = new ServiceCollection();
        services
            .AddSingleton(_world)
            .AddSingleton(_eventWorld)
            .AddSingleton(_metaWorld)
            .AddSingleton(_network)
            .AddSingleton(_assetsManager)
            .AddSingleton(_uiManager)
            .AddSingleton(_targetClientRpcDispatcher)
            .AddSingleton(_networkManager)
            .AddSingleton(_rpc)
            .AddSingleton(_modManager)
            .AddSingleton(_tween)
            .AddSingleton(_input)
            .AddSingleton(_drawer);
        _serviceProvider = services.BuildServiceProvider();
        
        _serviceProvider.Inject(_targetClientRpcDispatcher);
        _serviceProvider.Inject(_networkManager);
        _serviceProvider.Inject(_rpc);
        _serviceProvider.Inject(_modManager);
        _serviceProvider.Inject(_assetsManager);
        
        _modManager.Init(ModManager.Type.Client);
        _modManager.LoadMods(_assetsManager.ModsPath);

        // Инициализируем окно сначала
        Raylib.InitWindow(1024, 768, "Console Launcher");
        Raylib.SetWindowState(ConfigFlags.ResizableWindow);
        Raylib.SetWindowMinSize(400, 300);
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.Null);
        
        rlImGui.Setup();
        
        // Инициализируем новую UI систему
        var root = CreateDemoUI();
        _uiManager.SetRoot(root, _input);
        _uiManager.Font = Raylib.GetFontDefault();
        var codes = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя"
                    + "0123456789"
                    + ".,!?-+()[]{}:;/\\\"'`~@#$%^&*=_|<> "
                    + "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
                    + "▼" + "▶";
        int count = 0;
        var chars = Raylib.LoadCodepoints(codes, ref count);
        var font = Raylib.LoadFontEx("Pressstart2p.ttf", 32, chars, count);
        Console.WriteLine((bool)Raylib.IsFontValid(font));
        _uiManager.Font = Raylib.GetFontDefault();
        
        BaseSystem.InitWorlds(_world, _eventWorld, _metaWorld);
        _builder = EcsPipeline.New(_serviceProvider)
            .Inject(_world)
            .Inject(_eventWorld)
            .Inject(_metaWorld)
            .Inject(_modManager)
            .Inject(Camera.Main)
            .Inject(_network)
            .Inject(_rpc)
            .Inject(_assetsManager)
            .Inject(_tween)
            .Inject(_input)
            .Inject(_uiManager)
            .Inject(_drawer);

        InitEcs();

        _pipeline = _builder.Build();
        _pipeline.Init();
        
        _parallelRunner = _pipeline.GetRunner<EcsRunParallelRunner>();
        _parallelRunner.Init();

        Camera.Main.Position = new Vector3(10, 10, 10);
        Camera.Main.LookAt(Vector3.Zero);

        _input.KeyPressed += (key) =>
        {
            if (key == KeyboardKey.Escape)
            {
                if (_uiManager.Root.ComputedStyle.TryGetValue(StyleSheet.display, out var value))
                {
                    _uiManager.Root.SetInlineStyle(StyleSheet.display,
                        value == StyleSheet.display_none
                            ? StyleSheet.display_block
                            : StyleSheet.display_none);
                }
            }
        };
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        var packetType = (PacketType)reader.GetByte();
        if (packetType == PacketType.Snapshot)
        {
            _networkManager.ApplySnapshot(_world, reader);
        }
        else if (packetType == PacketType.Command)
        {
            _targetClientRpcDispatcher.Dispatch(reader);
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
        _input.Update();
        
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.DarkGreen);

        rlImGui.Begin();
        Raylib.BeginMode3D(Camera.Main.CameraReference);
        _network.PollEvents();
        _pipeline.Run();
        _pipeline.GetRunner<EcsPausableRunner>().PausableRun();
        _parallelRunner.RunParallel();
        _pipeline.GetRunner<PausableLateRunner>().PausableLateRun();
        BaseSystem.RunBuffers();
        //TODO: Добавить прием ивента от сервака на фиксед апдейт

        Raylib.EndMode3D();
        
        _uiManager.Update(Time.DeltaTime);
        _uiManager.Render(Time.DeltaTime);

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
        _serviceProvider.Dispose();
    }

    private void InitEcs()
    {
        _builder
            .AddRunner<EcsPausableRunner>()
            .AddRunner<PausableLateRunner>()
            .AddRunner<EcsRunParallelRunner>()
            .AddModule(new VisualModule())
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
        var root = new UIElement("root") { Classes = { "root-container" } };

        // --- 1. ХЕДЕР И ВЫПАДАЮЩЕЕ МЕНЮ ---
        var header = new UIElement { Classes = { "header" } };

        var homeLink = new UIElement { Classes = { "menu-item" }, Text = "Home" };

        // Создаем пункт меню, который будет открывать dropdown
        var fileMenuItem = new UIElement { Classes = { "menu-item" }, Text = "File" };

        // Создаем саму панель dropdown
        var dropdownPanel = new UIElement("file-dropdown") { Classes = { "dropdown-panel" } };
        dropdownPanel.AddChild(new UIElement { Classes = { "dropdown-item" }, Text = "New" });
        dropdownPanel.AddChild(new UIElement { Classes = { "dropdown-item" }, Text = "Open" });
        dropdownPanel.AddChild(new UIElement { Classes = { "dropdown-item" }, Text = "Save" });

        // Добавляем панель как дочерний элемент к пункту меню
        fileMenuItem.AddChild(dropdownPanel);

        var aboutLink = new UIElement { Classes = { "menu-item" }, Text = "About" };

        header.AddChild(homeLink);
        header.AddChild(fileMenuItem);
        header.AddChild(aboutLink);

        // --- 2. ОСНОВНОЙ КОНТЕНТ (старые тесты) ---
        var mainContent = new UIElement { Classes = { "main-content" } };

        // --- GROW TEST ---
        var growContainer = new UIElement { Classes = { "test-container", "grow-container" } };
        growContainer.AddChild(new UIElement { Classes = { "label", "test-item" }, Text = "Grow Test:" });
        growContainer.AddChild(new UIElement { Classes = { "test-item", "no-grow" }, Text = "Basis: 150px" });
        growContainer.AddChild(new UIElement { Classes = { "test-item", "grows-1" }, Text = "Grow: 1" });
        growContainer.AddChild(new UIElement { Classes = { "test-item", "grows-2" }, Text = "Grow: 2" });

        // --- SHRINK TEST ---
        var shrinkContainer = new UIElement { Classes = { "test-container", "shrink-container" } };
        shrinkContainer.AddChild(new UIElement { Classes = { "label", "test-item" }, Text = "Shrink Test:" });
        shrinkContainer.AddChild(new UIElement
            { Classes = { "test-item", "shrinks-1" }, Text = "Basis: 400, Shrink: 1" });
        shrinkContainer.AddChild(new UIElement
            { Classes = { "test-item", "shrinks-2" }, Text = "Basis: 200, Shrink: 2" });
        shrinkContainer.AddChild(new UIElement { Classes = { "test-item", "no-shrink" }, Text = "NO SHRINK" });

        // --- ALIGNMENT TEST И ТЕСТ ABSOLUTE ---
        var alignContainer = new UIElement { Classes = { "test-container", "align-container" } };
        alignContainer.AddChild(new UIElement { Classes = { "label", "test-item" }, Text = "Alignment & Absolute Test:" });
        alignContainer.AddChild(new UIElement { Classes = { "test-item", "align-center" }, Text = "Self: Center" });
        alignContainer.AddChild(new UIElement { Classes = { "test-item", "align-start" }, Text = "Self: Start" });

        // Создаем родительский элемент для значка
        var relativeParent = new UIElement { Classes = { "relative-parent" }, Text = "Relative Parent" };
        var notificationBadge = new UIElement { Classes = { "notification-badge" }, Text = "3" };
        relativeParent.AddChild(notificationBadge);

        // Добавляем этот элемент в сетку align-теста
        var stretchItem = new UIElement { Classes = { "test-item", "align-stretch" } }; // Без текста, чтобы не мешал
        stretchItem.AddChild(relativeParent); // Вкладываем relative-parent внутрь

        alignContainer.AddChild(stretchItem);
        
        var wrapContainer = new UIElement { Classes = { "test-container", "wrap-container" } };
        wrapContainer.AddChild(new UIElement { Classes = { "label", "test-item" }, Text = "Wrap Test:" });
        for (int i = 0; i < 10; i++)
        {
            wrapContainer.AddChild(new UIElement { Classes = { "test-item", "wrap-item" }, Text = $"Item {i + 1}" });
        }

        // Добавляем все тестовые контейнеры в mainContent
        mainContent.AddChild(growContainer);
        mainContent.AddChild(shrinkContainer);
        mainContent.AddChild(alignContainer);
        mainContent.AddChild(wrapContainer); // <-- Добавляем новый контейнер

        // --- 3. СБОРКА ИЕРАРХИИ ---
        root.AddChild(header);
        root.AddChild(mainContent);

        return root;
    }
}