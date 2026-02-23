using Karpik.Engine.Shared.Network.Core;
using LiteNetLib;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

public class LiteNetLibConnectionRequest : IConnectionRequest
{
    public ConnectionRequest Request { get; }
    
    public LiteNetLibConnectionRequest(ConnectionRequest request)
    {
        Request = request;
    }

    public IPeer AcceptIfKey(string key) => new LiteNetLibPeer(Request.AcceptIfKey(key));
}