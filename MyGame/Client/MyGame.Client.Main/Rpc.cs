using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Client.Main;

// Реализация интерфейса в конечном приложении
public class Rpc : IRpc
{
    [DI] private INetworkManager _netManager;
    private IWriter _writer;
                    
    private void OnInjected() => _writer = _netManager.CreateWriter();

    public IWriter GetWriter() => _writer;

    public void Send(DeliveryMethod deliveryMethod)
    {
        if (_netManager?.FirstPeer is { ConnectionState: ConnectionState.Connected })
        {
            _netManager.FirstPeer.Send(_writer, deliveryMethod);
        }
    }
}