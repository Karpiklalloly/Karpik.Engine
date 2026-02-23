namespace Karpik.Engine.Shared.Network.Core;

public interface IConnectionRequest
{
    public IPeer AcceptIfKey(string key);
}