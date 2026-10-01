using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Engine.Shared.Spatial2D;
using Microsoft.Extensions.Logging;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Server.CoinRush;

/// <summary>Runs authoritative match rules, scoring, hazards, and round transitions.</summary>
/// <param name="world">The authoritative entity world.</param>
/// <param name="eventWorld">The event world used to publish match events.</param>
/// <param name="physics">The physics world used for contact data and respawns.</param>
/// <param name="time">The engine fixed-step clock.</param>
/// <param name="log">The logger for match events and physics failures.</param>
public sealed class MatchSystem(
    EcsDefaultWorld world,
    EcsEventWorld eventWorld,
    IPhysicsWorld2D physics,
    Time time,
    ILogger<MatchSystem> log)
    : ISystemFixedUpdate
{
    /// <summary>Advances the match phase and rules for one fixed tick.</summary>
    public void FixedUpdate()
    {
        float dt = (float)time.FixedDeltaTime;
        var matches = world.Where(out MatchAspect matchAspect);
        for (int mi = 0; mi < matches.Count; mi++)
        {
            int e = matches[mi];
            ref MatchState m = ref matchAspect.States.Get(e);
            m.Tick++;

            switch (m.Phase)
            {
                case MatchPhase.WaitingForPlayers:
                    if (m.ConnectedPeers >= MatchRules.MaxPlayers)
                    {
                        m.Phase = MatchPhase.Countdown;
                        m.TimeLeft = m.CountdownDuration;
                        Enqueue(MatchEventKind.MatchStart, 0, m.Tick, 0f);
                        log.LogInformation("Match countdown started.");
                    }
                    break;
                case MatchPhase.Countdown:
                    m.TimeLeft = MatchRules.TickTimer(m.TimeLeft, dt);
                    if (m.TimeLeft <= 0f)
                    {
                        StartRound(ref m);
                    }
                    break;
                case MatchPhase.Running:
                    TickRunning(ref m, dt);
                    break;
                case MatchPhase.Finished:
                    m.TimeLeft = MatchRules.TickTimer(m.TimeLeft, dt);
                    if (ConsumeRestart())
                    {
                        ResetRound(ref m);
                    }
                    else if (m.TimeLeft <= 0f)
                    {
                        ResetRound(ref m);
                    }
                    break;
            }
            m.NetworkPhase = (byte)m.Phase;
        }
    }

    /// <summary>Updates active-round entities, scores, and the match timer.</summary>
    /// <param name="m">The match state to update.</param>
    /// <param name="dt">The fixed-step duration in seconds.</param>
    private void TickRunning(ref MatchState m, float dt)
    {
        m.TimeLeft = MatchRules.TickTimer(m.TimeLeft, dt);
        TickCoins(dt);

        bool anyContact = DrainContacts();

        var players = world.Where(out PlayerAspect playerAspect);
        for (int pi = 0; pi < players.Count; pi++)
        {
            ResolvePlayer(players[pi], anyContact, m.KillY, m.CoinRespawnDelay);
        }

        SyncScores(ref m);

        if (MatchRules.IsMatchOver(m.TimeLeft))
        {
            m.Phase = MatchPhase.Finished;
            m.Winner = MatchRules.Winner(m.Score0, m.Score1);
            m.TimeLeft = 5f;
            Enqueue(MatchEventKind.MatchFinish, 0, m.Winner, 0f);
            log.LogInformation("Match finished {S0}:{S1}, winner {W}.", m.Score0, m.Score1, m.Winner);
        }
    }

    /// <summary>Reads frame collisions to detect whether a player touched an entity.</summary>
    /// <returns><see langword="true"/> when a player appears in the contact list.</returns>
    private bool DrainContacts()
    {
        bool playerTouched = false;
        try
        {
            System.ReadOnlySpan<CollisionEvent> contacts = physics.GetFrameCollisions();
            if (contacts.Length == 0)
            {
                return false;
            }
            var playerPool = world.GetPool<Player>();
            for (int i = 0; i < contacts.Length; i++)
            {
                if (playerPool.Has(contacts[i].EntityA) || playerPool.Has(contacts[i].EntityB))
                {
                    playerTouched = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "GetFrameCollisions failed.");
        }
        return playerTouched;
    }

    /// <summary>Applies checkpoint, death, and coin zones to one player.</summary>
    /// <param name="playerEntity">The player entity to resolve.</param>
    /// <param name="contactArmed">Whether this frame contained a player contact.</param>
    /// <param name="killY">The level's fall-out threshold.</param>
    /// <param name="coinRespawnDelay">The delay before a collected coin reappears.</param>
    private void ResolvePlayer(int playerEntity, bool contactArmed, float killY, float coinRespawnDelay)
    {
        var transformPool = world.GetPool<Transform2D>();
        if (!transformPool.Has(playerEntity))
        {
            return;
        }
        Transform2D t = transformPool.Read(playerEntity);
        float px = (float)t.Position.X;
        float py = (float)t.Position.Y;

        ref Player p = ref world.GetPool<Player>().Get(playerEntity);

        var anchorPool = world.GetPool<RespawnAnchor>();
        var checkpoints = world.Where(out CheckpointAspect checkpointAspect);
        for (int ci = 0; ci < checkpoints.Count; ci++)
        {
            Checkpoint cp = checkpointAspect.Checkpoints.Read(checkpoints[ci]);
            float dx = px - cp.X;
            float dy = py - cp.Y;
            if ((dx * dx) + (dy * dy) <= cp.Radius * cp.Radius)
            {
                if (anchorPool.Has(playerEntity))
                {
                    ref RespawnAnchor a = ref anchorPool.Get(playerEntity);
                    if (a.X != cp.X || a.Y != cp.Y)
                    {
                        a.X = cp.X;
                        a.Y = cp.Y;
                        Enqueue(MatchEventKind.Respawn, p.Index, cp.ZoneIndex, 0f);
                    }
                }
            }
        }

        bool dead = MatchRules.FellOut(py, killY);
        if (!dead)
        {
            var spikes = world.Where(out SpikeAspect spikeAspect);
            for (int si = 0; si < spikes.Count; si++)
            {
                int s = spikes[si];
                Spike sp = spikeAspect.Spikes.Read(s);
                Transform2D st = spikeAspect.Transforms.Read(s);
                float sx = (float)st.Position.X;
                float sy = (float)st.Position.Y;
                if (MathF.Abs(px - sx) < (sp.Width / 2f) + 0.4f &&
                    MathF.Abs(py - sy) < (sp.Height / 2f) + 0.8f)
                {
                    dead = true;
                    break;
                }
            }
        }
        if (dead)
        {
            Respawn(playerEntity, ref p);
            return;
        }

        _ = contactArmed;
        var coins = world.Where(out CoinAspect coinAspect);
        for (int ci = 0; ci < coins.Count; ci++)
        {
            ref Coin coin = ref coinAspect.Coins.Get(coins[ci]);
            if (coin.Active == 0)
            {
                continue;
            }
            float dx = px - coin.X;
            float dy = py - coin.Y;
            if ((dx * dx) + (dy * dy) <= 1.1f * 1.1f)
            {
                coin.Active = 0;
                coin.RespawnIn = coinRespawnDelay;
                p.Score++;
                Enqueue(MatchEventKind.CoinPickup, p.Index, coin.SpawnIndex, 0f);
            }
        }
    }

    /// <summary>Returns a player to the active checkpoint and clears vertical velocity.</summary>
    /// <param name="playerEntity">The player entity to respawn.</param>
    /// <param name="p">The player's identity and score component.</param>
    private void Respawn(int playerEntity, ref Player p)
    {
        if (world.GetPool<MoveState>().Has(playerEntity))
            world.GetPool<MoveState>().Get(playerEntity).FallbackVelY = 0f;
        float x = LevelData.PlayerSpawns[p.Index].X;
        float y = LevelData.PlayerSpawns[p.Index].Y;
        if (world.GetPool<RespawnAnchor>().Has(playerEntity))
        {
            RespawnAnchor a = world.GetPool<RespawnAnchor>().Read(playerEntity);
            x = a.X;
            y = a.Y;
        }
        var pos = new OpenTK.Mathematics.Vector2d(x, y + 0.5);
        world.GetPool<TeleportRequest>().Add(playerEntity) = new TeleportRequest { Position = pos, Rotation = 0f };
        world.GetPool<SetVelocityRequest>().Add(playerEntity) = new SetVelocityRequest
        {
            Linear = new OpenTK.Mathematics.Vector2d(0, 0),
            Angular = 0,
        };
        if (world.GetPool<Transform2D>().Has(playerEntity))
        {
            Transform2D t = world.GetPool<Transform2D>().Read(playerEntity);
            t.Position = pos;
            world.GetPool<Transform2D>().Get(playerEntity) = t;
        }
        if (world.GetPool<PhysicsBodyRef>().Has(playerEntity))
        {
            PhysicsBodyRef bref = world.GetPool<PhysicsBodyRef>().Read(playerEntity);
            if (bref.Handle.IsValid)
            {
                try
                {
                    physics.SetVelocity(bref.Handle, new OpenTK.Mathematics.Vector2d(0, 0));
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Respawn velocity reset failed.");
                }
            }
        }
        Enqueue(MatchEventKind.Respawn, p.Index, 0, 0f);
    }

    /// <summary>Advances cooldowns and reactivates collected coins.</summary>
    /// <param name="dt">The fixed-step duration in seconds.</param>
    private void TickCoins(float dt)
    {
        var coins = world.Where(out CoinAspect coinAspect);
        for (int i = 0; i < coins.Count; i++)
        {
            ref Coin c = ref coinAspect.Coins.Get(coins[i]);
            if (c.Active != 0)
            {
                continue;
            }
            c.RespawnIn = MatchRules.TickCoin(c.RespawnIn, dt);
            if (MatchRules.CoinReady(c.RespawnIn))
            {
                c.Active = 1;
            }
        }
    }

    /// <summary>Copies player scores into the replicated match state.</summary>
    /// <param name="m">The match state to update.</param>
    private void SyncScores(ref MatchState m)
    {
        var players = world.Where(out PlayerAspect playerAspect);
        for (int i = 0; i < players.Count; i++)
        {
            int e = players[i];
            Player p = playerAspect.Players.Read(e);
            if (p.Index == 0)
            {
                m.Score0 = p.Score;
            }
            else if (p.Index == 1)
            {
                m.Score1 = p.Score;
            }
        }
    }

    /// <summary>Consumes pending restart requests from player input latches.</summary>
    /// <returns><see langword="true"/> when at least one player requested a restart.</returns>
    private bool ConsumeRestart()
    {
        bool any = false;
        var latches = world.Where(out LatchAspect latchAspect);
        for (int i = 0; i < latches.Count; i++)
        {
            ref InputLatch latch = ref latchAspect.Latches.Get(latches[i]);
            if (latch.Restart != 0)
            {
                any = true;
            }
            latch.Restart = 0;
        }
        return any;
    }

    /// <summary>Starts the active round and resets its timer.</summary>
    /// <param name="m">The match state to update.</param>
    private void StartRound(ref MatchState m)
    {
        m.Phase = MatchPhase.Running;
        m.TimeLeft = m.MatchDuration;
        Enqueue(MatchEventKind.MatchStart, 0, m.Tick, 0f);
        log.LogInformation("Round started, {Duration}s on the clock.", m.MatchDuration);
    }

    /// <summary>Resets scores, checkpoints, and coins for the next countdown.</summary>
    /// <param name="m">The match state to reset.</param>
    private void ResetRound(ref MatchState m)
    {
        m.Phase = MatchPhase.Countdown;
        m.TimeLeft = m.CountdownDuration;
        m.Score0 = 0;
        m.Score1 = 0;
        m.Winner = MatchRules.Draw;
        var players = world.Where(out PlayerAspect playerAspect);
        for (int i = 0; i < players.Count; i++)
        {
            int e = players[i];
            ref Player p = ref playerAspect.Players.Get(e);
            p.Score = 0;
            if (world.GetPool<MoveState>().Has(e))
                world.GetPool<MoveState>().Get(e).FallbackVelY = 0f;
            if (world.GetPool<RespawnAnchor>().Has(e))
            {
                ref RespawnAnchor a = ref world.GetPool<RespawnAnchor>().Get(e);
                a.X = a.SpawnX;
                a.Y = a.SpawnY;
                var pos = new OpenTK.Mathematics.Vector2d(a.X, a.Y);
                world.GetPool<TeleportRequest>().Add(e) = new TeleportRequest { Position = pos, Rotation = 0f };
            }
        }
        var coins = world.Where(out CoinAspect coinAspect);
        for (int i = 0; i < coins.Count; i++)
        {
            ref Coin c = ref coinAspect.Coins.Get(coins[i]);
            c.Active = 1;
            c.RespawnIn = 0f;
        }
        Enqueue(MatchEventKind.MatchStart, 0, m.Tick, 0f);
        log.LogInformation("Next round queued.");
    }

    /// <summary>Publishes a match event for transport delivery.</summary>
    /// <param name="kind">The event kind.</param>
    /// <param name="player">The affected player index.</param>
    /// <param name="arg">An event-specific integer value.</param>
    /// <param name="farg">An event-specific floating-point value.</param>
    private void Enqueue(MatchEventKind kind, int player, int arg, float farg)
    {
        int entity = eventWorld.NewEntity();
        eventWorld.GetPool<MatchEvent>().Add(entity) = new MatchEvent { Kind = kind, PlayerIndex = player, IntArg = arg, FloatArg = farg };
    }

    /// <summary>Selects replicated match state.</summary>
    private sealed class MatchAspect : EcsAspect
    {
        public EcsPool<MatchState> States = Inc;
    }

    /// <summary>Selects player components.</summary>
    private sealed class PlayerAspect : EcsAspect
    {
        public EcsPool<Player> Players = Inc;
    }

    /// <summary>Selects coin components.</summary>
    private sealed class CoinAspect : EcsAspect
    {
        public EcsPool<Coin> Coins = Inc;
    }

    /// <summary>Selects checkpoint components.</summary>
    private sealed class CheckpointAspect : EcsAspect
    {
        public EcsPool<Checkpoint> Checkpoints = Inc;
    }

    /// <summary>Selects spike zones and their positions.</summary>
    private sealed class SpikeAspect : EcsAspect
    {
        public EcsPool<Spike> Spikes = Inc;
        public EcsPool<Transform2D> Transforms = Inc;
    }

    /// <summary>Selects player input latches.</summary>
    private sealed class LatchAspect : EcsAspect
    {
        public EcsPool<InputLatch> Latches = Inc;
    }
}
