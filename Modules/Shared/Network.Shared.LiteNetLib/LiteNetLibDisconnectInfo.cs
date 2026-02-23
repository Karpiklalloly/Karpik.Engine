using Karpik.Engine.Shared.Network.Core;
using LiteNetLib;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

public class LiteNetLibDisconnectInfo : IDisconnectInfo
{
    public DisconnectInfo Info { get; }
    
    public LiteNetLibDisconnectInfo(DisconnectInfo info)
    {
        Info = info;
    }
}