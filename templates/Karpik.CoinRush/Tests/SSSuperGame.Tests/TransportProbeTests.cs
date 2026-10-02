using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib;
using LiteNetLib.Utils;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Characterizes raw LiteNetLib and engine transport behavior.</summary>
public sealed class TransportProbeTests
{
    /// <summary>Formats the transport disconnect reason for diagnostics.</summary>
    /// <param name="info">The transport disconnect information.</param>
    /// <returns>A readable disconnect reason.</returns>
    private static string DisconnectWhy(object info)
    {
        if (info is Karpik.Engine.Shared.Network.LiteNetLib.LiteNetLibDisconnectInfo d)
        {
            return d.Info.Reason.ToString() + "/" + d.Info.SocketErrorCode;
        }
        return info?.GetType().FullName ?? "null";
    }

    /// <summary>Verifies a burst of packets through raw LiteNetLib.</summary>
    [Fact]
    public async Task Raw_litenetlib_burst()
    {
        const int port = 14795;
        var serverListener = new LiteNetLib.EventBasedNetListener();
        var serverMgr = new LiteNetLib.NetManager(serverListener);
        var clientListener = new LiteNetLib.EventBasedNetListener();
        var clientMgr = new LiteNetLib.NetManager(clientListener);
        try
        {
            serverListener.ConnectionRequestEvent += req => req.AcceptIfKey("k");
            Assert.True(serverMgr.Start(port));
            Assert.True(clientMgr.Start());
            clientMgr.Connect("127.0.0.1", port, "k");
            var received = new List<int>();
            clientListener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                received.Add(reader.GetInt());
                reader.Recycle();
            };
            LiteNetLib.NetPeer? serverPeer = null;
            serverListener.PeerConnectedEvent += peer => serverPeer = peer;
            for (int i = 0; i < 200 && serverPeer is null; i++)
            {
                serverMgr.PollEvents();
                clientMgr.PollEvents();
                await Task.Delay(10);
            }
            Assert.NotNull(serverPeer);
            var writer = new NetDataWriter();
            for (int i = 0; i < 100; i++)
            {
                writer.Reset();
                writer.Put(i);
                serverPeer!.Send(writer, LiteNetLib.DeliveryMethod.Sequenced);
                serverMgr.PollEvents();
                clientMgr.PollEvents();
                await Task.Delay(5);
            }
            await Task.Delay(500);
            serverMgr.PollEvents();
            clientMgr.PollEvents();
            Assert.True(received.Count >= 90, $"Raw: only {received.Count}/100 arrived.");
        }
        finally
        {
            clientMgr.Stop();
            serverMgr.Stop();
        }
    }

    /// <summary>Verifies an idle engine transport connection carries a later packet.</summary>
    [Fact]
    public async Task Idle_connection_survives_and_single_packet_flows()
    {
        const int port = 14794;
        const long schema = 0x12345678L;
        var server = new LiteNetLibNetworkManager();
        var client = new LiteNetLibNetworkManager();
        try
        {
            server.ConfigureProtocolSchema(schema);
            client.ConfigureProtocolSchema(schema);
            server.ConnectionRequestEvent += req => req.AcceptIfKey("k");
            server.Start(port);
            client.Start(0);
            client.Connect("127.0.0.1", port, "k");
            INetworkManager[] all = { server, client };
            for (int i = 0; i < 200; i++)
            {
                foreach (INetworkManager m in all)
                {
                    m.PollEvents();
                }
                await Task.Delay(10);
                if (client.FirstPeer?.ConnectionState == ConnectionState.Connected)
                {
                    break;
                }
            }
            var notes = new List<string>();
            int got = 0;
            client.NetworkReceiveEvent += (peer, reader, channel, delivery) =>
            {
                try
                {
                    if (reader.AvailableBytes >= 4)
                    {
                        reader.GetInt();
                        got++;
                    }
                }
                finally
                {
                    reader.Recycle();
                }
            };
            client.PeerDisconnectedEvent += (peer, info) => notes.Add("client-disc:" + DisconnectWhy(info));
            server.PeerDisconnectedEvent += (peer, info) => notes.Add("server-disc:" + DisconnectWhy(info));

            await Task.Delay(1000);
            foreach (INetworkManager m in all)
            {
                m.PollEvents();
            }
            Assert.Equal(ConnectionState.Connected, client.FirstPeer?.ConnectionState);
            Assert.Equal(ConnectionState.Connected, server.FirstPeer?.ConnectionState);

            IWriter w = server.CreateWriter();
            w.Put(123456);
            server.SendToAll(w, DeliveryMethod.ReliableOrdered);
            for (int i = 0; i < 100; i++)
            {
                foreach (INetworkManager m in all)
                {
                    m.PollEvents();
                }
                await Task.Delay(10);
            }
            Assert.Equal(ConnectionState.Connected, client.FirstPeer?.ConnectionState);
            Assert.Equal(ConnectionState.Connected, server.FirstPeer?.ConnectionState);
            Assert.Empty(notes);
            Assert.True(got >= 1, "Single settled packet should arrive.");
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }
}
