using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Game.Generated;
using Game.Generated.Client;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.EcsRunners;
using Karpik.Engine.Shared.Modding;
using LiteNetLib;
using Network;
using Raylib_cs;
using rlImGui_cs;
using Position = Karpik.Engine.Client.UIToolkit.Position;

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
        root.StyleSheet = new StyleSheet();
        root.StyleSheet.AddClass("custom-header", new Style()
        {
            BackgroundColor = new Color(76, 175, 80, 255)
        });
        
        // Добавляем псевдоклассы для демонстрации
        root.StyleSheet.AddHover("custom-header", new Style()
        {
            BackgroundColor = new Color(56, 142, 60, 255) // Темнее при наведении
        });
        
        {
            var panel = new VisualElement();
            panel.AddClass("panel");
            {
                var header = new Label("KarpikEngine UI Demo");
                header.AddClass("header");
                header.AddClass("custom-header");
                panel.AddChild(header);

                var content = new VisualElement();
                content.AddClass("content");
                panel.AddChild(content);
                {
                    // Секция текстового ввода
                    var inputLabel = new Label("Text Input:");
                    content.AddChild(inputLabel);
                    
                    var textInput = new TextInput("Enter your name...");
                    textInput.OnTextChanged += (text) => Logger.Instance.Log($"Text changed: {text}");
                    textInput.OnEnterPressed += () => Logger.Instance.Log("Enter pressed!");
                    content.AddChild(textInput);
                    
                    var checkbox1 = new Checkbox("Enable notifications");
                    checkbox1.OnCheckedChanged += (isChecked) => Logger.Instance.Log($"Notifications: {isChecked}");
                    content.AddChild(checkbox1);
                    
                    var slider1 = new Slider(0f, 100f, 50f);
                    content.AddChild(slider1);
                    
                    // Секция прогресс-баров
                    var progressLabel = new Label("Progress Bars:");
                    content.AddChild(progressLabel);
                    
                    var progressBar1 = new ProgressBar(0f, 100f, 75f);
                    progressBar1.Text = "Loading...";
                    content.AddChild(progressBar1);
                    
                    // Секция уведомлений
                    var toastLabel = new Label("Toast Notifications:");
                    content.AddChild(toastLabel);
                    
                    var toastButton1 = new Button("Show Info Toast");
                    toastButton1.OnClick += () => _uiManager.ShowToast("This is an info message!", ToastType.Info);
                    content.AddChild(toastButton1);
                    
                    // Секция слоев и модальных окон
                    var layersLabel = new Label("Layers & Modals:");
                    content.AddChild(layersLabel);
                    
                    var modalButton = new Button("Show Modal Dialog");
                    modalButton.OnClick += ShowModalDemo;
                    content.AddChild(modalButton);
                    
                    var tooltipButton = new Button("Hover for Tooltip");
                    tooltipButton.AddManipulator(new TooltipManipulator("This is a helpful tooltip that appears on hover!"));
                    content.AddChild(tooltipButton);
                }
            }
            root.AddChild(panel);
        }
        
        _uiManager.SetRoot(root);
    }
    
    private void ShowModalDemo()
    {
        var modal = new Modal("Demo Modal Window");
        modal.Size = new Vector2(400, 300);
        
        // Создаем содержимое модального окна
        var modalContent = new VisualElement();
        modalContent.Style.FlexDirection = FlexDirection.Column;
        
        var welcomeLabel = new Label("Welcome to the modal dialog!");
        welcomeLabel.AddClass("label");
        modalContent.AddChild(welcomeLabel);
        
        var descriptionLabel = new Label("This is a demonstration of the layered UI system.");
        descriptionLabel.AddClass("label");
        modalContent.AddChild(descriptionLabel);
        
        var inputField = new TextInput("Enter some text...");
        modalContent.AddChild(inputField);
        
        var buttonContainer = new VisualElement();
        buttonContainer.Style.FlexDirection = FlexDirection.Row;
        buttonContainer.Style.JustifyContent = JustifyContent.SpaceEvenly;
        buttonContainer.Style.Margin = new Margin(0, 10, 0, 0);
        
        var okButton = new Button("OK");
        okButton.OnClick += () => 
        {
            Logger.Instance.Log("CLICK");
            _uiManager.ShowToast($"You entered: {inputField.Text}", ToastType.Success);
            modal.Close();
        };
        okButton.AddClass("button");
        okButton.Style.Margin = new Margin(5);
        buttonContainer.AddChild(okButton);
        
        var cancelButton = new Button("Cancel");
        cancelButton.OnClick += () => modal.Close();
        cancelButton.AddClass("button");
        cancelButton.Style.Margin = new Margin(5);
        buttonContainer.AddChild(cancelButton);
        
        modalContent.AddChild(buttonContainer);
        modal.SetContent(modalContent);
        
        _uiManager.ShowModal(modal, true);
    }
}