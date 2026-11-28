using Karpik.Engine.Shared.Network.Core;
using LiteNetLib;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

public sealed class LiteNetLibConnectionRequest : IConnectionRequest
{
    private readonly LiteNetLibNetworkManager _owner;
    private readonly ConnectionRequest _request;

    internal LiteNetLibConnectionRequest(LiteNetLibNetworkManager owner, ConnectionRequest request)
    {
        _owner = owner;
        _request = request;
    }

    public IPeer? AcceptIfKey(string key) => _owner.AcceptIfKey(_request, key);
}
