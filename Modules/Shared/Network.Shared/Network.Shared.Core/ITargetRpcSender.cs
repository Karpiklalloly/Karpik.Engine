namespace Karpik.Engine.Shared.Network.Core;

public interface ITargetRpcSender
{
    public IWriter GetWriter();
    public INetworkManager GetManager();
}