using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib;
using Karpik.Engine.Shared.Spatial2D;
using Microsoft.Extensions.Logging;
using OpenTK.Mathematics;
using SSSuperGame.Shared.CoinRush;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks two-client communication with the real LiteNetLib transport.</summary>
public sealed class NetIntegrationTests
{
    private const int Port = 14799;
    private const string Key = "coinrush-test";

    /// <summary>Finds the repository root from the solution file.</summary>
    /// <returns>The absolute repository root path.</returns>
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "SSSuperGame.slnx")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Repo root not found.");
    }

    /// <summary>Loads the server assembly built for the integration test.</summary>
    /// <returns>The loaded server assembly.</returns>
    private static Assembly ServerAssembly()
    {
        string path = Path.Combine(RepoRoot(), "Source", "SSSuperGame.Server", "bin", "Debug", "net10.0", "SSSuperGame.Server.dll");
        Assert.True(File.Exists(path), "Build the Server project first.");
        return Assembly.LoadFrom(path);
    }

    /// <summary>Captures server logs for integration-test diagnostics.</summary>
    /// <typeparam name="T">The logger category.</typeparam>
    private sealed class TestLogger<T> : ILogger<T>
    {
        public readonly List<string> Lines = new();
        /// <summary>Returns no scope for the test logger.</summary>
        /// <typeparam name="TState">The scope state type.</typeparam>
        /// <param name="state">The scope state.</param>
        /// <returns>No scope.</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>Indicates whether the test logger accepts messages.</summary>
        /// <param name="logLevel">The severity of the log entry.</param>
        /// <returns>Whether logging is enabled.</returns>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>Records a formatted test log entry.</summary>
        /// <typeparam name="TState">The log state type.</typeparam>
        /// <param name="logLevel">The severity of the log entry.</param>
        /// <param name="eventId">The event identifier.</param>
        /// <param name="state">The log state or scope state.</param>
        /// <param name="exception">The optional associated exception.</param>
        /// <param name="formatter">Formats the log state and exception.</param>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
            {
                Lines.Add(logLevel + " " + formatter(state, exception) + (exception is null ? "" : " | " + exception.GetType().Name + ": " + exception.Message));
            }
        }
    }

    /// <summary>Creates the real server networking system with test dependencies.</summary>
    /// <param name="world">The ECS world used by the test.</param>
    /// <param name="manager">The manager.</param>
    /// <param name="server">The server assembly.</param>
    /// <param name="logger">Receives the constructed test logger.</param>
    /// <returns>The constructed networking system.</returns>
    private static object NewServerNet(EcsDefaultWorld world, INetworkManager manager, Assembly server, out object logger)
    {
        Type? t = server.GetType("SSSuperGame.Server.CoinRush.ServerNetSystem");
        Assert.NotNull(t);
        logger = Activator.CreateInstance(typeof(TestLogger<>).MakeGenericType(t!))!;
        var config = new NetworkConfig { Address = "127.0.0.1", Port = Port, Key = Key };
        var eventWorld = new EcsEventWorld();
        Type snapshotType = typeof(MatchState).Assembly.GetType("Karpik.Engine.Generated.NetworkSnapshotRegistry")!;
        object snapshots = Activator.CreateInstance(snapshotType)!;
        return Activator.CreateInstance(t!, world, eventWorld, snapshots, manager, config, logger!)!;
    }

    /// <summary>Adds a player at its configured spawn point.</summary>
    /// <param name="world">The ECS world used by the test.</param>
    /// <param name="index">The player slot index.</param>
    private static void SpawnPlayer(EcsDefaultWorld world, int index)
    {
        SpawnDesc s = LevelData.PlayerSpawns[index];
        int e = world.NewEntity();
        world.GetPool<Player>().Add(e) = new Player { Index = index, Score = index == 0 ? 2 : 0, PeerId = -1 };
        world.GetPool<MoveState>().Add(e) = new MoveState { LastInputSequence = -1 };
        world.GetPool<NetState>().Add(e) = new NetState { X = s.X, Y = s.Y };
        world.GetPool<RespawnAnchor>().Add(e) = new RespawnAnchor { X = s.X, Y = s.Y };
        world.GetPool<Transform2D>().Add(e) = new Transform2D { Position = new Vector2d(s.X, s.Y) };
    }

    /// <summary>Polls network managers for the requested interval.</summary>
    /// <param name="managers">The network managers to poll.</param>
    /// <param name="millis">The polling duration in milliseconds.</param>
    /// <returns>A task that completes after the polling interval.</returns>
    private static async Task PumpAsync(IEnumerable<INetworkManager> managers, int millis)
    {
        long end = Environment.TickCount64 + millis;
        while (Environment.TickCount64 < end)
        {
            foreach (INetworkManager m in managers)
            {
                m.PollEvents();
            }
            await Task.Delay(5);
        }
    }

    /// <summary>Counts players assigned to network peers.</summary>
    /// <param name="world">The ECS world used by the test.</param>
    /// <returns>The number of bound player slots.</returns>
    private static int BoundSlots(EcsDefaultWorld world)
    {
        int n = 0;
        var players = world.Where(out PlayerSlotAspect aspect);
        for (int i = 0; i < players.Count; i++)
        {
            if (aspect.Players.Read(players[i]).PeerId >= 0)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>Selects player entities for peer-slot checks.</summary>
    private sealed class PlayerSlotAspect : EcsAspect
    {
        public EcsPool<Player> Players = Inc;
    }

    /// <summary>Verifies two clients bind slots, exchange input, and receive snapshots.</summary>
    [Fact]
    public async Task Two_clients_connect_exchange_and_receive()
    {
        Assembly server = ServerAssembly();
        var world = new EcsDefaultWorld();
        var serverManager = new LiteNetLibNetworkManager();
        var clientA = new LiteNetLibNetworkManager();
        var clientB = new LiteNetLibNetworkManager();
        try
        {
            int match = world.NewEntity();
            world.GetPool<MatchState>().Add(match) = new MatchState { Phase = MatchPhase.WaitingForPlayers, Tick = 42 };
            world.GetPool<MatchState>().Get(match).CountdownDuration = 8f;
            SpawnPlayer(world, 0);
            SpawnPlayer(world, 1);
            int coin = world.NewEntity();
            world.GetPool<Coin>().Add(coin) = new Coin { SpawnIndex = 0, X = 0f, Y = 0f, Active = 1 };
            int coin2 = world.NewEntity();
            world.GetPool<Coin>().Add(coin2) = new Coin { SpawnIndex = 5, X = 1f, Y = 1f, Active = 0, RespawnIn = 5f };

            object netSys = NewServerNet(world, serverManager, server, out object serverLog);
            ((ISystemInit)netSys).Init();
            serverManager.ConnectionRequestEvent += req => req.AcceptIfKey(Key);
            const long schema = 0x12345678L;
            serverManager.ConfigureProtocolSchema(schema);
            clientA.ConfigureProtocolSchema(schema);
            clientB.ConfigureProtocolSchema(schema);
            serverManager.Start(Port);

            int snapsA = 0;
            var eventsA = new List<MatchEvent>();
            int snapsB = 0;
            var eventsB = new List<MatchEvent>();
            var rawA = new List<string>();
            var rawB = new List<string>();
            clientA.NetworkReceiveEvent += (peer, reader, channel, delivery) =>
            {
                try
                {
                    rawA.Add("ch" + channel + "/" + delivery + " avail" + reader.AvailableBytes);
                    if (reader.AvailableBytes < 1)
                    {
                        return;
                    }
                    byte kind = reader.GetByte();
                    if (kind == PacketKind.Snapshot) snapsA++;
                    else if (kind == PacketKind.Event && MatchEvent.TryReadBody(reader, out MatchEvent e))
                    {
                        eventsA.Add(e);
                    }
                }
                finally
                {
                    reader.Recycle();
                }
            };
            clientB.NetworkReceiveEvent += (peer, reader, channel, delivery) =>
            {
                try
                {
                    rawB.Add("ch" + channel + "/" + delivery + " avail" + reader.AvailableBytes);
                    if (reader.AvailableBytes < 1)
                    {
                        return;
                    }
                    byte kind = reader.GetByte();
                    if (kind == PacketKind.Snapshot) snapsB++;
                    else if (kind == PacketKind.Event && MatchEvent.TryReadBody(reader, out MatchEvent e))
                    {
                        eventsB.Add(e);
                    }
                }
                finally
                {
                    reader.Recycle();
                }
            };
            clientA.Start(0);
            clientB.Start(0);
            clientA.Connect("127.0.0.1", Port, Key);
            clientB.Connect("127.0.0.1", Port, Key);
            INetworkManager[] all = { serverManager, clientA, clientB };

            bool bound = false;
            for (int i = 0; i < 200 && !bound; i++)
            {
                await PumpAsync(all, 50);
                bound = BoundSlots(world) == 2;
            }
            Assert.True(bound, "Both clients should bind player slots.");
            Assert.Equal(MatchPhase.Countdown, world.GetPool<MatchState>().Read(match).Phase);
            Assert.Equal(8f, world.GetPool<MatchState>().Read(match).TimeLeft);
            await PumpAsync(all, 1000);
            for (int i = 0; i < 40; i++)
            {
                ((ISystemFixedUpdate)netSys).FixedUpdate();
                await PumpAsync(all, 50);
                if (eventsA.Any(e => e.Kind == MatchEventKind.FullState) && eventsB.Any(e => e.Kind == MatchEventKind.FullState))
                {
                    break;
                }
            }
            int slotA = eventsA.Where(e => e.Kind == MatchEventKind.FullState).Select(e => e.PlayerIndex).FirstOrDefault(-1);
            int slotB = eventsB.Where(e => e.Kind == MatchEventKind.FullState).Select(e => e.PlayerIndex).FirstOrDefault(-1);
            string allA = string.Join(",", eventsA.Where(e => e.Kind == MatchEventKind.FullState).Select(e => e.PlayerIndex));
            string allB = string.Join(",", eventsB.Where(e => e.Kind == MatchEventKind.FullState).Select(e => e.PlayerIndex));
            if (slotA < 0 || slotB < 0 || slotA == slotB)
            {
                var lines = (System.Collections.IList)serverLog.GetType().GetField("Lines")!.GetValue(serverLog)!;
                var dump = new List<string>();
                foreach (object? l in lines)
                {
                    dump.Add(l?.ToString() ?? "?");
                }
                Assert.Fail("Slots not distributed A=" + slotA + "(" + allA + ") B=" + slotB + "(" + allB + ")"
                    + " rawA=[" + string.Join(",", rawA) + "] rawB=[" + string.Join(",", rawB) + "]"
                    + " snapsA=" + snapsA + " snapsB=" + snapsB
                    + ". Server log:\n" + string.Join("\n", dump));
            }

            int seqForSlot0 = slotA == 0 ? 7 : 3;
            int seqForSlot1 = slotA == 1 ? 7 : 3;
            float axisForSlot0 = slotA == 0 ? -1f : 1f;
            var cmdA = new InputCommand { Sequence = 7, MoveAxis = -1f, Jump = true, RestartRequest = false };
            IWriter wa = clientA.CreateWriter();
            cmdA.Write(wa);
            clientA.FirstPeer!.Send(wa, InputCommand.Delivery);
            var cmdB = new InputCommand { Sequence = 3, MoveAxis = 1f, Jump = false, RestartRequest = false };
            IWriter wb = clientB.CreateWriter();
            cmdB.Write(wb);
            clientB.FirstPeer!.Send(wb, InputCommand.Delivery);
            await PumpAsync(all, 500);

            var latchPool = world.GetPool<InputLatch>();
            var playerPool = world.GetPool<Player>();
            int seq0 = -1, seq1 = -1;
            float axis0 = 0f;
            foreach (int e in world.Entities)
            {
                if (!playerPool.Has(e) || !latchPool.Has(e))
                {
                    continue;
                }
                InputLatch latch = latchPool.Read(e);
                if (playerPool.Read(e).Index == 0)
                {
                    seq0 = latch.Sequence;
                    axis0 = latch.MoveAxis;
                }
                else
                {
                    seq1 = latch.Sequence;
                }
            }
            Assert.Equal(seqForSlot0, seq0);
            Assert.Equal(axisForSlot0, axis0);
            Assert.Equal(seqForSlot1, seq1);

            INetworkManager slot0Client = slotA == 0 ? clientA : clientB;
            var stale = new InputCommand { Sequence = 1, MoveAxis = 0f, Jump = false, RestartRequest = false };
            IWriter ws = slot0Client.CreateWriter();
            stale.Write(ws);
            slot0Client.FirstPeer!.Send(ws, InputCommand.Delivery);
            await PumpAsync(all, 300);
            foreach (int e in world.Entities)
            {
                if (playerPool.Has(e) && latchPool.Has(e) && playerPool.Read(e).Index == 0)
                {
                    Assert.Equal(seqForSlot0, latchPool.Read(e).Sequence);
                }
            }

            ((ISystemFixedUpdate)netSys).FixedUpdate();
            ((ISystemFixedUpdate)netSys).FixedUpdate();
            ((ISystemFixedUpdate)netSys).FixedUpdate();
            await PumpAsync(all, 500);
            Assert.True(snapsA > 0);
            Assert.True(snapsB > 0);

        }
        finally
        {
            clientA.Stop();
            clientB.Stop();
            serverManager.Stop();
            world.Destroy();
        }
    }
}
