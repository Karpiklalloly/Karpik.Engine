using System.Numerics;
using DCFApixels.DragonECS;
using Karpik.Engine.Client;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Client.Main;

[Module]
public class MyGameClientInstaller : IModule
{
    public string Name => "MyGame.Client.Main";
    
    private INetworkManager _networkManager;
    private NetworkManager _manager;
    private EcsDefaultWorld _world;
    private TargetClientRpcDispatcher _targetClientRpcDispatcher;

    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register<IRpc>(new Rpc());
        services.Register(new TargetClientRpcDispatcher());
        services.Register(new Drawer());
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        _networkManager = services.Get<INetworkManager>();
        _world = services.Get<EcsDefaultWorld>();
        _targetClientRpcDispatcher = services.Get<TargetClientRpcDispatcher>();

        var window = services.Get<IWindow>();
        var renderer = services.Get<IRenderer>();

        window.Init(102, 768, "My Game");
        window.SetWindowState(WindowFlags.ResizableWindow);
        window.SetWindowMinSize(400, 300);
        window.SetTargetFPS(60);
        renderer.MainCamera3D.Position = new Vector3(10, 10, 10);
        renderer.MainCamera3D.LookAt(Vector3.Zero);
        var uiManager = services.Get<UIManager>();
        
        services.Get<Input>().KeyPressed += (key) =>
        {
            if (key == KeyboardKeys.Escape)
            {
                if (uiManager.Root.ComputedStyle.TryGetValue(StyleSheet.display, out var value))
                {
                    uiManager.Root.SetInlineStyle(StyleSheet.display,
                        value == StyleSheet.display_none
                            ? StyleSheet.display_block
                            : StyleSheet.display_none);
                }
            }
        };

        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
        _networkManager.NetworkReceiveEvent += NetworkManagerOnNetworkReceiveEvent;
        _networkManager.PeerConnectedEvent += NetworkManagerOnPeerConnectedEvent;
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
    
    private void NetworkManagerOnPeerConnectedEvent(IPeer peer)
    {
        Console.WriteLine("Disconnected from server. Clearing world...");
        _manager.ClearClientCache();
    }
}