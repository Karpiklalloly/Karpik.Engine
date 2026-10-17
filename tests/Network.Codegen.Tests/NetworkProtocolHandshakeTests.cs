using System.Diagnostics;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib;
using LiteNetLib;
using LiteNetLib.Utils;
using Xunit;
using CoreDeliveryMethod = Karpik.Engine.Shared.Network.Core.DeliveryMethod;
using LiteNetDeliveryMethod = LiteNetLib.DeliveryMethod;

namespace Network.Codegen.Tests;

public sealed class NetworkProtocolHandshakeTests
{
    private const long TestSchemaHash = 0x1234_5678_7654_3210;

    [Fact]
    public void Equal_schema_hashes_complete_handshake_before_payload_delivery()
    {
        using var server = new LiteNetLibNetworkManager();
        using var client = new LiteNetLibNetworkManager();
        server.ConfigureProtocolSchema(TestSchemaHash);
        client.ConfigureProtocolSchema(TestSchemaHash);
        server.ConnectionRequestEvent += static request => request.AcceptIfKey("schema-test");
        var serverConnected = false;
        var clientConnected = false;
        var payloadReceived = false;
        server.PeerConnectedEvent += _ => serverConnected = true;
        client.PeerConnectedEvent += _ => clientConnected = true;
        server.NetworkReceiveEvent += (_, reader, _, deliveryMethod) =>
        {
            Assert.True(serverConnected);
            Assert.Equal(CoreDeliveryMethod.ReliableOrdered, deliveryMethod);
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
        client.FirstPeer!.Send(writer, CoreDeliveryMethod.ReliableOrdered);
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

    [Fact]
    public void Snapshot_sent_before_handshake_is_rejected_without_exposing_reader()
    {
        AssertRawPacketRejected(writer =>
        {
            writer.Put((byte)PacketType.Snapshot);
            writer.Put(123456);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void Truncated_handshake_payload_is_rejected(int schemaBytes)
    {
        AssertRawPacketRejected(writer =>
        {
            writer.Put((byte)PacketType.Handshake);
            for (var index = 0; index < schemaBytes; index++)
            {
                writer.Put((byte)(0x44 + index));
            }
        });
    }

    [Fact]
    public void Matching_handshake_with_trailing_byte_is_rejected()
    {
        AssertRawPacketRejected(writer =>
        {
            writer.Put((byte)PacketType.Handshake);
            writer.Put(TestSchemaHash);
            writer.Put((byte)0xFF);
        });
    }

    private static void AssertRawPacketRejected(Action<NetDataWriter> writePacket)
    {
        using var server = new LiteNetLibNetworkManager();
        var clientListener = new EventBasedNetListener();
        var client = new NetManager(clientListener);
        server.ConfigureProtocolSchema(TestSchemaHash);
        server.ConnectionRequestEvent += static request => request.AcceptIfKey("schema-test");
        var packetSent = false;
        var connectedEvents = 0;
        var payloadEvents = 0;
        var disconnected = false;
        server.PeerConnectedEvent += _ => connectedEvents++;
        server.NetworkReceiveEvent += (_, _, _, _) => payloadEvents++;
        server.PeerDisconnectedEvent += (_, _) => disconnected = true;
        clientListener.NetworkReceiveEvent += static (_, reader, _, _) => reader.Recycle();
        clientListener.PeerConnectedEvent += peer =>
        {
            var writer = new NetDataWriter();
            writePacket(writer);
            peer.Send(writer, LiteNetDeliveryMethod.ReliableOrdered);
            packetSent = true;
        };
        clientListener.PeerDisconnectedEvent += (_, _) => disconnected = true;

        var port = server.GetFreePort();
        server.Start(port);
        Assert.True(client.Start());
        try
        {
            client.Connect("127.0.0.1", port, "schema-test");
            PumpUntil(server, client, () => disconnected);
        }
        finally
        {
            client.Stop();
        }

        Assert.True(packetSent);
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

    private static void PumpUntil(
        LiteNetLibNetworkManager server,
        NetManager client,
        Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            server.PollEvents();
            client.PollEvents();
            Thread.Sleep(1);
        }
        Assert.True(condition(), "Timed out waiting for malformed/pre-handshake packet rejection.");
    }
}
