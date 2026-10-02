using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Engine.Shared.Spatial2D;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTK.Mathematics;
using SSSuperGame.Shared.CoinRush;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks authoritative match timing, scoring, death, and reset behavior.</summary>
public sealed class ServerMatchTests
{
    /// <summary>Adds a player and its gameplay components to the test world.</summary>
    /// <param name="world">The ECS world used by the test.</param>
    /// <param name="index">The player slot index.</param>
    /// <param name="x">The horizontal spawn coordinate.</param>
    /// <param name="y">The vertical spawn coordinate.</param>
    private static void Spawn(EcsDefaultWorld world, int index, float x, float y)
    {
        int e = world.NewEntity();
        world.GetPool<Player>().Add(e) = new Player { Index = index, Score = 0, PeerId = index };
        world.GetPool<MoveState>().Add(e) = new MoveState();
        world.GetPool<NetState>().Add(e) = new NetState { X = x, Y = y };
        world.GetPool<RespawnAnchor>().Add(e) = new RespawnAnchor { X = x, Y = y };
        world.GetPool<Transform2D>().Add(e) = new Transform2D { Position = new Vector2d(x, y) };
    }

    /// <summary>Verifies score aggregation and winner selection for two players.</summary>
    [Fact]
    public void Two_players_score_and_winner_is_decided()
    {
        EcsDefaultWorld world = new();
        try
        {
            int match = world.NewEntity();
            world.GetPool<MatchState>().Add(match) = new MatchState
            {
                Phase = MatchPhase.Running,
                TimeLeft = 0.05f,
                ConnectedPeers = 2,
            };
            Spawn(world, 0, -11f, 2f);
            Spawn(world, 1, 11f, 2f);

            var playerPool = world.GetPool<Player>();
            int coin = world.NewEntity();
            world.GetPool<Coin>().Add(coin) = new Coin { SpawnIndex = 0, X = -10.5f, Y = 2f, Active = 1 };

            foreach (int e in world.Entities)
            {
                if (!playerPool.Has(e) || playerPool.Read(e).Index != 0)
                {
                    continue;
                }
                ref Player p = ref playerPool.Get(e);
                p.Score += 3;
            }
            foreach (int e in world.Entities)
            {
                if (!playerPool.Has(e) || playerPool.Read(e).Index != 1)
                {
                    continue;
                }
                ref Player p = ref playerPool.Get(e);
                p.Score += 1;
            }

            ref MatchState m = ref world.GetPool<MatchState>().Get(match);
            m.TimeLeft = MatchRules.TickTimer(m.TimeLeft, 0.1f);
            Assert.True(MatchRules.IsMatchOver(m.TimeLeft));

            int s0 = 0, s1 = 0;
            foreach (int e in world.Entities)
            {
                if (!playerPool.Has(e))
                {
                    continue;
                }
                Player p = playerPool.Read(e);
                if (p.Index == 0)
                {
                    s0 = p.Score;
                }
                else
                {
                    s1 = p.Score;
                }
            }
            m.Score0 = s0;
            m.Score1 = s1;
            m.Winner = MatchRules.Winner(s0, s1);
            m.Phase = MatchPhase.Finished;

            Assert.Equal(3, m.Score0);
            Assert.Equal(1, m.Score1);
            Assert.Equal(0, m.Winner);
            Assert.Equal(MatchPhase.Finished, m.Phase);
        }
        finally
        {
            world.Destroy();
        }
    }

    /// <summary>Verifies equal scores produce a draw.</summary>
    [Fact]
    public void Draw_when_scores_equal()
    {
        Assert.Equal(MatchRules.Draw, MatchRules.Winner(7, 7));
    }

    /// <summary>Verifies configured timers, kill height, and spawn survive round reset.</summary>
    [Fact]
    public void Configured_round_duration_and_spawn_survive_reset()
    {
        var world = new EcsDefaultWorld();
        var events = new EcsEventWorld();
        try
        {
            int match = world.NewEntity();
            world.GetPool<MatchState>().Add(match) = new MatchState
            {
                Phase = MatchPhase.Countdown,
                MatchDuration = 11f,
                CountdownDuration = 4f,
                CoinRespawnDelay = 6f,
                KillY = -10f,
            };
            int player = world.NewEntity();
            world.GetPool<Player>().Add(player) = new Player { Index = 0 };
            world.GetPool<RespawnAnchor>().Add(player) = new RespawnAnchor
            {
                X = 99f, Y = 99f, SpawnX = 3f, SpawnY = 4f,
            };
            world.GetPool<Transform2D>().Add(player) = new Transform2D { Position = new Vector2d(3f, 4f) };
            int coin = world.NewEntity();
            world.GetPool<Coin>().Add(coin) = new Coin { X = 3f, Y = 4f, Active = 1 };

            string? dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "SSSuperGame.slnx")))
                dir = Directory.GetParent(dir)?.FullName;
            Assert.NotNull(dir);
            string dll = Path.Combine(dir, "Source", "SSSuperGame.Server", "bin", "Debug", "net10.0", "SSSuperGame.Server.dll");
            Type type = Assembly.LoadFrom(dll).GetType("SSSuperGame.Server.CoinRush.MatchSystem")!;
            object logger = Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type), nonPublic: true)!;
            var time = new Time();
            typeof(Time).GetProperty("FixedDeltaTime")!.GetSetMethod(true)!.Invoke(time, new object[] { 1.0 / 60.0 });
            var system = (ISystemFixedUpdate)Activator.CreateInstance(type, world, events, new PhysicsSmoothnessTests.StubPhysics(), time, logger)!;

            system.FixedUpdate();
            Assert.Equal(11f, world.GetPool<MatchState>().Read(match).TimeLeft);
            ref MatchState state = ref world.GetPool<MatchState>().Get(match);
            system.FixedUpdate();
            Assert.Equal(6f, world.GetPool<Coin>().Read(coin).RespawnIn);
            state.KillY = 5f;
            system.FixedUpdate();
            Assert.Equal(99.5, world.GetPool<Transform2D>().Read(player).Position.Y);
            state.Phase = MatchPhase.Finished;
            state.TimeLeft = 0f;
            system.FixedUpdate();

            Assert.Equal(4f, state.TimeLeft);
            Assert.Equal(3f, world.GetPool<RespawnAnchor>().Read(player).X);
            Assert.Equal(4f, world.GetPool<RespawnAnchor>().Read(player).Y);
            Assert.Equal(new Vector2d(3f, 4f), world.GetPool<TeleportRequest>().Read(player).Position);
        }
        finally
        {
            world.Destroy();
        }
    }

}
