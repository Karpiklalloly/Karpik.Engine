using Karpik.Engine.Shared.Network.Core;
using LiteNetLib;
using ConnectionState = Karpik.Engine.Shared.Network.Core.ConnectionState;
using DeliveryMethod = Karpik.Engine.Shared.Network.Core.DeliveryMethod;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

public class LiteNetLibPeer : IPeer
{
    public NetPeer Peer { get; }
    
    public LiteNetLibPeer(NetPeer peer)
    {
        Peer = peer;
    }

    public int Id => Peer.Id;
    public ConnectionState ConnectionState => (ConnectionState)Peer.ConnectionState;

    public void Send(IWriter writer, DeliveryMethod deliveryMethod)
    {
        Peer.Send(((LiteNetLibWriter)writer).Writer, (global::LiteNetLib.DeliveryMethod)deliveryMethod);
    }
}