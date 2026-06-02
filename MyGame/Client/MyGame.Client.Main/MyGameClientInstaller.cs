using DCFApixels.DragonECS;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Network.Core;
using Veldrid;

namespace Karpik.Engine.MyGame.Client.Main;

[Module]
public class MyGameClientInstaller : IInstaller, IInstallerDestroy, IInstallerConfiguratable
{
    public string Name => "MyGame.Client.Main";
    
    private INetworkManager _networkManager = null!;
    private NetworkManager _manager = new();
    private EcsDefaultWorld _world = null!;
    private TargetClientRpcDispatcher _targetClientRpcDispatcher = null!;
    private ClientReconnectTokenStore _reconnectTokenStore = null!;

    private Input _input = null!;
    private Action<Key> _onInput = null!;

    private IServiceContainer _container = null!;

    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer)
    {
        services.Register<IRpc>(new Rpc());
        services.Register(new TargetClientRpcDispatcher());
        services.Register(new Drawer());
        services.Register(new ClientReconnectTokenStore());
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        _manager.Initialize();
        _networkManager = services.Get<INetworkManager>();
        _world = services.Get<EcsDefaultWorld>();
        _targetClientRpcDispatcher = services.Get<TargetClientRpcDispatcher>();
        _reconnectTokenStore = services.Get<ClientReconnectTokenStore>();

        // var renderer = services.Get<IRenderer>();
        //
        // renderer.MainCamera3D.Position = new Vector3(10, 10, 10);
        // renderer.MainCamera3D.LookAt(Vector3.Zero);
        //
        // // Create 2D camera entity with ECS
        // var cameraEntity = _world.NewEntity();
        // ref var cam2D = ref _world.GetPool<Camera2DComponent>().Add(cameraEntity);
        // cam2D.Position = new Vector2(0, 0);
        // cam2D.TargetPosition = new Vector2(0, 0);
        // cam2D.Zoom = 10f;
        // cam2D.Rotation = 0f;
        // cam2D.ViewportSize = new Vector2(1024, 768);
        // cam2D.SmoothingFactor = 0.1f;
        // _world.GetPool<ActiveCamera2DTag>().Add(cameraEntity);
        //
        // var uiManager = services.Get<UIManager>();
        //
        // _onInput = key =>
        // {
        //     if (key == Key.Escape)
        //     {
        //         if (uiManager.Root.ComputedStyle.TryGetValue(StyleSheet.display, out var value))
        //         {
        //             uiManager.Root.SetInlineStyle(StyleSheet.display,
        //                 value == StyleSheet.display_none
        //                     ? StyleSheet.display_block
        //                     : StyleSheet.display_none);
        //         }
        //     }
        // };

        _input = services.Get<Input>();
        _input.KeyPressed += _onInput;

        module = new DemoModuleClient();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        _container = services;
        _networkManager.NetworkReceiveEvent += NetworkManagerOnNetworkReceiveEvent;
        _networkManager.PeerConnectedEvent += NetworkManagerOnPeerConnectedEvent;
        _networkManager.PeerDisconnectedEvent += NetworkManagerOnPeerDisconnectedEvent;

        var peer = _networkManager.FirstPeer;
        if (peer is { ConnectionState: ConnectionState.Connected })
        {
            SendHandshake(peer);
        }
    }

    private void NetworkManagerOnPeerConnectedEvent(IPeer peer)
    {
        SendHandshake(peer);
    }

    private void SendHandshake(IPeer peer)
    {
        var writer = _networkManager.CreateWriter();
        var reconnectToken = GetReconnectToken();
        _reconnectTokenStore.Save(reconnectToken);
        writer.Put((byte)PacketType.Handshake);
        writer.Put(reconnectToken);
        peer.Send(writer, DeliveryMethod.ReliableOrdered);
        Console.WriteLine($"[MyGameClientInstaller] Sent handshake with reconnect token {reconnectToken}");
    }

    private long GetReconnectToken()
    {
        var clientSessionPool = _world.GetPool<ClientReconnectSession>();
        foreach (var entity in _world.Where(EcsStaticMask.Inc<ClientReconnectSession>().Build()))
        {
            var token = clientSessionPool.Get(entity).ReconnectToken;
            if (token > 0)
            {
                return token;
            }
        }

        var sessionPool = _world.GetPool<PlayerSession>();

        foreach (var entity in _world.Where(EcsStaticMask.Inc<PlayerSession>().Inc<LocalPlayer>().Build()))
        {
            var token = sessionPool.Get(entity).ReconnectToken;
            if (token > 0)
            {
                return token;
            }
        }

        foreach (var entity in _world.Where(EcsStaticMask.Inc<PlayerSession>().Build()))
        {
            var token = sessionPool.Get(entity).ReconnectToken;
            if (token > 0)
            {
                return token;
            }
        }

        return _reconnectTokenStore.Load();
    }
    
    private void NetworkManagerOnNetworkReceiveEvent(IPeer peer, IReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        var packetType = (PacketType)reader.GetByte();
        if (packetType == PacketType.Snapshot)
        {
            _manager.ApplySnapshot(_world, reader);
        }
        else if (packetType == PacketType.Command)
        {
            _targetClientRpcDispatcher.Dispatch(reader);
        }

        reader.Recycle();
    }
    
    private void NetworkManagerOnPeerDisconnectedEvent(IPeer peer, IDisconnectInfo info)
    {
        Console.WriteLine("Disconnected from server. Clearing world...");
        _manager.ClearClientCache();
    }

    public void Destroy()
    {
        _networkManager.NetworkReceiveEvent -= NetworkManagerOnNetworkReceiveEvent;
        _networkManager.PeerConnectedEvent -= NetworkManagerOnPeerConnectedEvent;
        _networkManager.PeerDisconnectedEvent -= NetworkManagerOnPeerDisconnectedEvent;

        _input.KeyPressed -= _onInput;
        _onInput = null!;
    }
}
