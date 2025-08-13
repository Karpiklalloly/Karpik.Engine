using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Game.Generated;
using Game.Generated.Client;
using GTweens.Extensions;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Client.UI.Extensions;
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
        // Создаем корневой элемент с глобальными стилями
        var root = new VisualElement("root");
        {
            var panel = new VisualElement();
            panel.AddClass("panel");
            root.AddChild(panel);
            {
                var content = new VisualElement();
                content.AddClass("content");
                content.Style.FlexDirection = FlexDirection.Column;
                panel.AddChild(content);
                {
                    // Демонстрация новой системы твинов
                    var tweenLabel = new Label("Tween Animation Demo:");
                    tweenLabel.Style.BackgroundColor = Color.White;
                    tweenLabel.Style.TextColor = Color.Black;
                    content.AddChild(tweenLabel);
                    
                    // Анимируемый элемент
                    var animatedBox = new VisualElement("AnimatedBox");
                    animatedBox.Size = new Vector2(50, 50);
                    animatedBox.Style.BackgroundColor = Color.Yellow;
                    animatedBox.Style.BorderRadius = 0.9f;
                    content.AddChild(animatedBox);
                    
                    // Кнопки для демонстрации различных анимаций
                    var buttonContainer = new VisualElement("ButtonContainer");
                    buttonContainer.Style.BackgroundColor = new Color(0, 0, 0, 128);
                    buttonContainer.Style.BorderRadius = 0.5f;
                    buttonContainer.Style.FlexDirection = FlexDirection.Row;
                    buttonContainer.Style.JustifyContent = JustifyContent.SpaceBetween;
                    
                    // Кнопки управления анимациями
                    var fadeInBtn = new Button("Fade In");
                    fadeInBtn.Style.Margin = new Margin(5);
                    fadeInBtn.OnClick += () => animatedBox.FadeIn(0.5f);
                    buttonContainer.AddChild(fadeInBtn);
                    
                    var fadeOutBtn = new Button("Fade Out");
                    fadeOutBtn.Style.Margin = new Margin(5);
                    fadeOutBtn.OnClick += () => animatedBox.FadeOut(0.5f);
                    buttonContainer.AddChild(fadeOutBtn);
                    
                    var scaleInBtn = new Button("Scale In");
                    scaleInBtn.Style.Margin = new Margin(5);
                    scaleInBtn.OnClick += () => animatedBox.ScaleIn(0.5f);
                    buttonContainer.AddChild(scaleInBtn);
                    
                    var shakeBtn = new Button("Shake");
                    shakeBtn.Style.Margin = new Margin(5);
                    shakeBtn.OnClick += () => animatedBox.Shake(30f, 1.5f); // Увеличиваем интенсивность и время
                    buttonContainer.AddChild(shakeBtn);
                    
                    var pulseBtn = new Button("Pulse");
                    pulseBtn.Style.Margin = new Margin(5);
                    pulseBtn.OnClick += () => animatedBox.Pulse(1.3f, 0.6f);
                    buttonContainer.AddChild(pulseBtn);
                    
                    var slideBtn = new Button("Slide");
                    slideBtn.Style.Margin = new Margin(5);
                    slideBtn.OnClick += () => animatedBox.SlideIn(new Vector2(-200, -50), 1.0f); // Увеличиваем смещение и время
                    buttonContainer.AddChild(slideBtn);
                    
                    var colorBtn = new Button("Color");
                    colorBtn.Style.Margin = new Margin(5);
                    colorBtn.OnClick += () => 
                    {
                        var colors = new[] { Color.Red, Color.Green, Color.Blue, Color.Yellow, Color.Purple };
                        var randomColor = colors[new Random().Next(colors.Length)];
                        animatedBox.TweenBackgroundColor(randomColor, 0.5f);
                    };
                    buttonContainer.AddChild(colorBtn);
                    
                    var moveBtn = new Button("Move");
                    moveBtn.Style.Margin = new Margin(5);
                    moveBtn.OnClick += () => 
                    {
                        var newPos = new Vector2(
                            new Random().Next(50, 500),
                            new Random().Next(50, 150)
                        );
                        animatedBox.TweenPosition(newPos, 0.5f);
                    };
                    buttonContainer.AddChild(moveBtn);
                    
                    var testBtn = new Button("Test");
                    testBtn.Style.Margin = new Margin(5);
                    testBtn.OnClick += () =>
                    {
                        var targetPos = animatedBox.Position + new Vector2(100, 50);

                        animatedBox.TweenPosition(targetPos, 1.0f);
                    };
                    buttonContainer.AddChild(testBtn);
                    
                    content.AddChild(buttonContainer);
                }
            }
        }
        
        _uiManager.SetRoot(root);
    }
}