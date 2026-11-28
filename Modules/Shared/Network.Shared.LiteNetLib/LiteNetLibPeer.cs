using Karpik.Engine.Shared.Network.Core;
using LiteNetLib;
using ConnectionState = Karpik.Engine.Shared.Network.Core.ConnectionState;
using DeliveryMethod = Karpik.Engine.Shared.Network.Core.DeliveryMethod;

namespace Karpik.Engine.Shared.Network.LiteNetLib;

internal enum PeerWireMode : byte
{
    Legacy,
    AwaitingServerHandshake,
    Revision,
}

public sealed class LiteNetLibPeer : IPeer
{
    internal LiteNetLibPeer(LiteNetLibNetworkManager owner, NetPeer peer)
    {
        Owner = owner;
        RawPeer = peer;
    }

    internal LiteNetLibNetworkManager Owner { get; }
    internal NetPeer RawPeer { get; }
    internal PeerWireMode WireMode { get; set; }
    internal bool SchemaValidated { get; set; }

    public int Id => RawPeer.Id;
    public ConnectionState ConnectionState => (ConnectionState)RawPeer.ConnectionState;

    public void Disconnect() => RawPeer.Disconnect();

    public void Send(IWriter writer, DeliveryMethod deliveryMethod) => Owner.SendGameplay(this, writer, deliveryMethod);
}
