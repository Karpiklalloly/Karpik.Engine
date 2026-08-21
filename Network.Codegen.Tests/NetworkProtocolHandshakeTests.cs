using System.Diagnostics;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib;
using Xunit;

namespace Network.Codegen.Tests;

public sealed class NetworkProtocolHandshakeTests
{
    [Fact]
    public void Equal_schema_hashes_complete_handshake_before_payload_delivery()
    {
        using var server = new LiteNetLibNetworkManager();
        using var client = new LiteNetLibNetworkManager();
        const long schemaHash = 0x1234_5678_7654_3210;
        server.ConfigureProtocolSchema(schemaHash);
        client.ConfigureProtocolSchema(schemaHash);
        server.ConnectionRequestEvent += static request => request.AcceptIfKey("schema-test");
        var serverConnected = false;
        var clientConnected = false;
        var payloadReceived = false;
        server.PeerConnectedEvent += _ => serverConnected = true;
        client.PeerConnectedEvent += _ => clientConnected = true;
        server.NetworkReceiveEvent += (_, reader, _, deliveryMethod) =>
        {
            Assert.True(serverConnected);
            Assert.Equal(DeliveryMethod.ReliableOrdered, deliveryMethod);
            Assert.Equal(PacketType.Snapshot, (PacketType)reader.GetByte());
            Assert.Equal(987654321, reader.GetInt());
            payloadReceived = true;
        };

        var port = server.GetFreePort();
        server.Start(port);
        client.Start(0);
        client.Connect("127.0.0.1", port, "schema-test");
        PumpUntil(server, client, () => serverConnected && clientConnected);

        var writer = client.CreateWriter();
        writer.Put((byte)PacketType.Snapshot);
        writer.Put(987654321);
        client.FirstPeer!.Send(writer, DeliveryMethod.ReliableOrdered);
        PumpUntil(server, client, () => payloadReceived);
        Assert.True(payloadReceived);
    }

    [Fact]
    public void Mismatched_schema_disconnects_without_exposing_payload_or_connected_event()
    {
        using var server = new LiteNetLibNetworkManager();
        using var client = new LiteNetLibNetworkManager();
        server.ConfigureProtocolSchema(101);
        client.ConfigureProtocolSchema(202);
        server.ConnectionRequestEvent += static request => request.AcceptIfKey("schema-test");
        var connectedEvents = 0;
        var payloadEvents = 0;
        var disconnected = false;
        server.PeerConnectedEvent += _ => connectedEvents++;
        client.PeerConnectedEvent += _ => connectedEvents++;
        server.NetworkReceiveEvent += (_, _, _, _) => payloadEvents++;
        client.NetworkReceiveEvent += (_, _, _, _) => payloadEvents++;
        server.PeerDisconnectedEvent += (_, _) => disconnected = true;
        client.PeerDisconnectedEvent += (_, _) => disconnected = true;

        var port = server.GetFreePort();
        server.Start(port);
        client.Start(0);
        client.Connect("127.0.0.1", port, "schema-test");
        PumpUntil(server, client, () => disconnected);

        Assert.True(disconnected);
        Assert.Equal(0, connectedEvents);
        Assert.Equal(0, payloadEvents);
    }

    private static void PumpUntil(
        LiteNetLibNetworkManager server,
        LiteNetLibNetworkManager client,
        Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            server.PollEvents();
            client.PollEvents();
            Thread.Sleep(1);
        }
        Assert.True(condition(), "Timed out waiting for the LiteNetLib protocol handshake.");
    }
}
