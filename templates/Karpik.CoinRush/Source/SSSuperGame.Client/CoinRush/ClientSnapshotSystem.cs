using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Generated;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib;
using Microsoft.Extensions.Logging;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Applies server snapshots and events, then interpolates client display positions.</summary>
/// <param name="world">Client world receiving replicated components.</param>
/// <param name="eventWorld">Event world receiving match events.</param>
/// <param name="snapshots">Registry that applies encoded snapshots.</param>
/// <param name="net">Network manager providing received packets.</param>
/// <param name="time">Clock used for interpolation and snapshot age.</param>
/// <param name="log">Logger for malformed packets.</param>
[SequentialSystem]
public sealed class ClientSnapshotSystem(
    EcsDefaultWorld world,
    EcsEventWorld eventWorld,
    NetworkSnapshotRegistry snapshots,
    INetworkManager net,
    Time time,
    ILogger<ClientSnapshotSystem> log)
    : ISystemInit, ISystemUpdate, ISystemDestroy
{
    /// <summary>Gets the age of the latest snapshot in seconds, or -1 before one arrives.</summary>
    public double SnapshotAge
    {
        get
        {
            var sessions = world.Where(out SessionAspect aspect);
            if (sessions.Count == 0) return -1;
            ClientSessionState state = aspect.Sessions.Read(sessions[0]);
            return state.HasSnapshot ? time.TotalTime - state.ReceivedAt : -1;
        }
    }
    /// <summary>Gets the tick of the latest snapshot, or -1 before one arrives.</summary>
    public int LastTick
    {
        get
        {
            var sessions = world.Where(out SessionAspect aspect);
            return sessions.Count == 0 ? -1 : aspect.Sessions.Read(sessions[0]).LatestTick;
        }
    }

    /// <summary>Subscribes to network receive and disconnect events.</summary>
    public void Init()
    {
        net.NetworkReceiveEvent += OnReceive;
        net.PeerDisconnectedEvent += OnPeerDrop;
    }

    /// <summary>Unsubscribes from network events.</summary>
    public void Destroy()
    {
        net.NetworkReceiveEvent -= OnReceive;
        net.PeerDisconnectedEvent -= OnPeerDrop;
    }

    /// <summary>Clears stale snapshot and slot state after a peer disconnects.</summary>
    /// <param name="peer">Disconnected peer.</param>
    /// <param name="info">Disconnect details.</param>
    private void OnPeerDrop(IPeer peer, IDisconnectInfo info)
    {
        var sessions = world.Where(out SessionAspect sessionAspect);
        if (sessions.Count > 0)
        {
            ref ClientSessionState session = ref sessionAspect.Sessions.Get(sessions[0]);
            session.HasSnapshot = false;
            session.LatestTick = -1;
            session.PreviousTick = -1;
            session.ReceivedAt = 0;
            session.PendingLocalSlot = -1;
        }
        var locals = world.Where(out LocalAspect aspect);
        for (int i = 0; i < locals.Count; i++) aspect.Locals.Get(locals[i]).Slot = -1;
    }

    /// <summary>Parses gameplay packets and applies snapshots or queues match events.</summary>
    /// <param name="peer">Peer that sent the packet.</param>
    /// <param name="reader">Packet reader recycled after processing.</param>
    /// <param name="channel">Network channel carrying the packet.</param>
    /// <param name="delivery">Delivery mode used for the packet.</param>
    private void OnReceive(IPeer peer, IReader reader, byte channel, DeliveryMethod delivery)
    {
        try
        {
            if (channel != NetChannel.Gameplay || reader.AvailableBytes < 1) return;
            byte kind = reader.GetByte();
            if (kind == PacketKind.Snapshot)
            {
                var sessions = world.Where(out SessionAspect sessionAspect);
                if (sessions.Count == 0) return;
                ref ClientSessionState session = ref sessionAspect.Sessions.Get(sessions[0]);
                CapturePrevious();
                snapshots.ApplySnapshot(world, reader);
                EnsureDisplays();
                session.PreviousTick = session.LatestTick;
                var matches = world.Where(out MatchAspect matchAspect);
                if (matches.Count == 0) return;
                session.LatestTick = matchAspect.States.Read(matches[0]).Tick;
                session.ReceivedAt = time.TotalTime;
                session.HasSnapshot = true;
                return;
            }
            if (kind == PacketKind.Event && MatchEvent.TryReadBody(reader, out MatchEvent ev))
            {
                if (ev.Kind == MatchEventKind.FullState)
                {
                    ApplyLocalSlot(ev.PlayerIndex);
                }
                int eventEntity = eventWorld.NewEntity();
                eventWorld.GetPool<MatchEvent>().Add(eventEntity) = ev;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Snapshot parse failed.");
        }
        finally
        {
            reader.Recycle();
        }
    }

    /// <summary>Remembers replicated positions before the next snapshot is applied.</summary>
    private void CapturePrevious()
    {
        var players = world.Where(out PlayerAspect aspect);
        var netPool = world.GetPool<NetState>();
        var displays = world.GetPool<DisplayState>();
        for (int i = 0; i < players.Count; i++)
        {
            int e = players[i];
            if (!netPool.Has(e) || !displays.Has(e)) continue;
            NetState state = netPool.Read(e);
            ref DisplayState display = ref displays.Get(e);
            display.PreviousSnapshotX = state.X;
            display.PreviousSnapshotY = state.Y;
        }
    }

    /// <summary>Adds presentation state to newly replicated player entities.</summary>
    private void EnsureDisplays()
    {
        var players = world.Where(out PlayerAspect aspect);
        var displays = world.GetPool<DisplayState>();
        for (int i = 0; i < players.Count; i++)
        {
            int e = players[i];
            ref Player player = ref aspect.Players.Get(e);
            if (displays.Has(e)) continue;
            LevelData.PlayerColor(player.Index, out player.R, out player.G, out player.B);
            displays.Add(e) = new DisplayState { ScalePulse = 1f };
        }
    }

    /// <summary>Assigns the local slot now or defers until its entity exists.</summary>
    /// <param name="slot">Server-assigned player slot.</param>
    private void ApplyLocalSlot(int slot)
    {
        if ((uint)slot >= MatchRules.MaxPlayers) return;
        var sessions = world.Where(out SessionAspect sessionAspect);
        if (sessions.Count == 0) return;
        ref ClientSessionState session = ref sessionAspect.Sessions.Get(sessions[0]);
        session.PendingLocalSlot = slot;

        var locals = world.Where(out LocalAspect aspect);
        if (locals.Count == 0) return;
        for (int i = 0; i < locals.Count; i++)
        {
            aspect.Locals.Get(locals[i]).Slot = slot;
        }
        session.PendingLocalSlot = -1;
    }

    /// <summary>Updates match diagnostics and interpolates player display positions.</summary>
    public void Update()
    {
        var sessions = world.Where(out SessionAspect sessionAspect);
        if (sessions.Count == 0) return;
        ref ClientSessionState session = ref sessionAspect.Sessions.Get(sessions[0]);
        if (session.PendingLocalSlot >= 0)
        {
            ApplyLocalSlot(session.PendingLocalSlot);
        }
        if (!session.HasSnapshot) return;
        var matches = world.Where(out MatchAspect matchAspect);
        for (int i = 0; i < matches.Count; i++)
        {
            ref MatchState state = ref matchAspect.States.Get(matches[i]);
            state.Phase = (MatchPhase)state.NetworkPhase;
            state.SnapTick = state.Tick;
            state.SnapshotAge = (float)(time.TotalTime - session.ReceivedAt);
        }

        float progress = session.PreviousTick >= 0
            ? MatchRules.SnapshotProgress((float)(time.TotalTime - session.ReceivedAt),
                MatchRules.SnapshotInterval(session.LatestTick - session.PreviousTick, (float)time.FixedDeltaTime))
            : 1f;
        var players = world.Where(out PlayerAspect playerAspect);
        var displays = world.GetPool<DisplayState>();
        var netPool = world.GetPool<NetState>();
        for (int i = 0; i < players.Count; i++)
        {
            int e = players[i];
            if (!netPool.Has(e)) continue;
            NetState state = netPool.Read(e);
            ref DisplayState display = ref displays.Get(e);
            display.PrevX = display.X;
            display.PrevY = display.Y;
            display.X = session.PreviousTick >= 0 ? MatchRules.Lerp(display.PreviousSnapshotX, state.X, progress) : state.X;
            display.Y = session.PreviousTick >= 0 ? MatchRules.Lerp(display.PreviousSnapshotY, state.Y, progress) : state.Y;
        }
    }

    /// <summary>Selects replicated match state.</summary>
    private sealed class MatchAspect : EcsAspect
    {
        public EcsPool<MatchState> States = Inc;
    }

    /// <summary>Selects replicated players.</summary>
    private sealed class PlayerAspect : EcsAspect
    {
        public EcsPool<Player> Players = Inc;
    }

    /// <summary>Selects the local player slot.</summary>
    private sealed class LocalAspect : EcsAspect
    {
        public EcsPool<LocalPlayer> Locals = Inc;
    }

    /// <summary>Selects client connection and snapshot state.</summary>
    private sealed class SessionAspect : EcsAspect
    {
        public EcsPool<ClientSessionState> Sessions = Inc;
    }
}

/// <summary>Maps match events to client effects and transfers effects into the display world.</summary>
/// <param name="world">Client world receiving effect entities.</param>
/// <param name="eventWorld">Event world used to publish mapped effects.</param>
public sealed class ClientEventSystem(EcsDefaultWorld world, EcsEventWorld eventWorld)
    : ISystemUpdate, IEcsRunOnEvents<MatchEvent>, IEcsRunOnEvents<FxEvent>
{
    /// <summary>Converts server match events into local effect events.</summary>
    /// <param name="events">Match events in the current batch.</param>
    public void RunOnEvents(Span<MatchEvent> events)
    {
        for (int i = 0; i < events.Length; i++)
        {
            MatchEvent ev = events[i];
            if (ev.Kind == MatchEventKind.FullState)
            {
                continue;
            }
            FxKind kind = ev.Kind switch
            {
                MatchEventKind.MatchStart or MatchEventKind.MatchFinish => FxKind.MatchEnd,
                MatchEventKind.CoinPickup => FxKind.CoinPickup,
                MatchEventKind.Respawn => FxKind.Death,
                _ => (FxKind)byte.MaxValue,
            };
            if (kind != (FxKind)byte.MaxValue)
            {
                int eventEntity = eventWorld.NewEntity();
                eventWorld.GetPool<FxEvent>().Add(eventEntity) = new FxEvent { Kind = kind, PlayerIndex = ev.PlayerIndex, Age = 0f, Duration = kind == FxKind.MatchEnd ? 2.5f : 0.6f };
            }
        }
    }

    /// <summary>Participates in the client update phase.</summary>
    public void Update() { }

    /// <summary>Copies effect events into persistent client display entities.</summary>
    /// <param name="events">Effect events in the current batch.</param>
    public void RunOnEvents(Span<FxEvent> events)
    {
        for (int i = 0; i < events.Length; i++)
        {
            int entity = world.NewEntity();
            FxEvent fx = events[i];
            fx.Age = 0f;
            world.GetPool<FxEvent>().Add(entity) = fx;
        }
    }
}
