using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Server.Main;

public class TargetRpcSender : ITargetRpcSender, IOnInjectedDI
{
    [DI] private INetworkManager _netManager = null!;
    private IWriter _writer = null!;
                    
    public void OnInjected() => _writer = _netManager.CreateWriter();

    public IWriter GetWriter() => _writer;
    public INetworkManager GetManager() => _netManager;
}