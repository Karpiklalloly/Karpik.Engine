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
    private EcsRunParallelRunner _parallelRunner;
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
        Raylib.InitWindow(1024, 768, "Console Launcher");
        Raylib.SetWindowState(ConfigFlags.ResizableWindow);
        Raylib.SetWindowMinSize(400, 300);
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.Null);
        
        rlImGui.Setup();
        
        // Инициализируем новую UI систему
        UIManager = new UIManager();
        var root = CreateDemoUI();
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
        UIManager.Font = Raylib.GetFontDefault();
        
        BaseSystem.InitWorlds(Worlds.Instance.World, Worlds.Instance.EventWorld, Worlds.Instance.MetaWorld);
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
        
        _parallelRunner = _pipeline.GetRunner<EcsRunParallelRunner>();
        _parallelRunner.Init();
        Worlds.Instance.Init(_pipeline);

        Camera.Main.Position = new Vector3(10, 10, 10);
        Camera.Main.LookAt(Vector3.Zero);

        Input.KeyPressed += (key) =>
        {
            if (key == KeyboardKey.Escape)
            {
                if (UIManager.Root.ComputedStyle.TryGetValue(StyleSheet.display, out var value))
                {
                    UIManager.Root.SetInlineStyle(StyleSheet.display,
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
        
        UIManager.Update(Time.DeltaTime);
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
            .AddRunner<EcsRunParallelRunner>()
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
        // И прикрепляем манипулятор, который будет ею управлять
        //fileMenuItem.AddManipulator(new DropdownManipulator("file-dropdown"));

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