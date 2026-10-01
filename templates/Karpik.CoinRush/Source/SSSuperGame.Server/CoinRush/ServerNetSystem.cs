using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Generated;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.Network.Core;
using Microsoft.Extensions.Logging;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Server.CoinRush;

/// <summary>Binds network peers to player slots and exchanges input and match snapshots.</summary>
/// <param name="world">The authoritative entity world.</param>
/// <param name="eventWorld">The event world for match-start notifications.</param>
/// <param name="snapshots">The registry that serializes replicated ECS state.</param>
/// <param name="net">The engine network manager.</param>
/// <param name="config">The configured network address, port, and key.</param>
/// <param name="log">The logger for transport activity and failures.</param>
public sealed class ServerNetSystem(
    EcsDefaultWorld world,
    EcsEventWorld eventWorld,
    NetworkSnapshotRegistry snapshots,
    INetworkManager net,
    NetworkConfig config,
    ILogger<ServerNetSystem> log)
    : ISystemInit, ISystemFixedUpdate, ISystemDestroy
{
    /// <summary>Resets peer bindings and subscribes to transport events.</summary>
    public void Init()
    {
        EnsureConfigDefaults();
        ResetTransportBindings();
        net.PeerConnectedEvent += OnPeerConnected;
        net.PeerDisconnectedEvent += OnPeerDisconnected;
        net.NetworkReceiveEvent += OnReceive;
        net.ConnectionRequestEvent += OnConnectionRequest;
        log.LogInformation("CoinRush server transport subscribed ({Addr}:{Port}).", config.Address, config.Port);
    }

    /// <summary>Unsubscribes from transport events during shutdown.</summary>
    public void Destroy()
    {
        net.PeerConnectedEvent -= OnPeerConnected;
        net.PeerDisconnectedEvent -= OnPeerDisconnected;
        net.NetworkReceiveEvent -= OnReceive;
        net.ConnectionRequestEvent -= OnConnectionRequest;
    }

    /// <summary>Clears stale peer assignments after a worker restart.</summary>
    private void ResetTransportBindings()
    {
        var peerPool = world.GetPool<ServerPeerState>();
        var players = world.Where(out PlayerAspect aspect);
        for (int i = 0; i < players.Count; i++)
        {
            ref Player p = ref aspect.Players.Get(players[i]);
            p.PeerId = -1;
            if (peerPool.Has(players[i])) peerPool.TryDel(players[i]);
        }
        var matches = world.Where(out MatchAspect aspect2);
        for (int i = 0; i < matches.Count; i++)
        {
            ref MatchState m = ref aspect2.States.Get(matches[i]);
            m.ConnectedPeers = 0;
            m.BroadcastCountdown = 0;
        }
    }
    /// <summary>Fills in default address, port, and key values when absent.</summary>
    private void EnsureConfigDefaults()
    {
        if (string.IsNullOrWhiteSpace(config.Address))
        {
            config.Address = "127.0.0.1";
        }
        if (config.Port <= 0)
        {
            config.Port = CoinRushContent.DefaultMatch().ServerPort;
        }
        if (string.IsNullOrWhiteSpace(config.Key))
        {
            config.Key = CoinRushContent.DefaultMatch().ServerKey;
        }
    }

    /// <summary>Accepts a connection request when its key matches the match configuration.</summary>
    /// <param name="request">The pending connection request.</param>
    private void OnConnectionRequest(IConnectionRequest request)
    {
        try
        {
            request.AcceptIfKey(config.Key);
            log.LogInformation("Connection request accepted (key match).");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Connection request handling failed.");
        }
    }

    /// <summary>Assigns a connected peer to a player slot and schedules full-state delivery.</summary>
    /// <param name="peer">The connected peer.</param>
    private void OnPeerConnected(IPeer peer)
    {
        try
        {
            int slot = BindSlot(peer.Id);
            log.LogInformation("Peer {Id} bound to slot {Slot}.", peer.Id, slot);
            RefreshConnectedCount();
            if (slot >= 0)
            {
                SetPeer(slot, peer);
                MaybeStartCountdown();
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "PeerConnected handling failed.");
        }
    }

    /// <summary>Releases a disconnected peer's player slot.</summary>
    /// <param name="peer">The disconnected peer.</param>
    /// <param name="info">Transport details about the disconnect.</param>
    private void OnPeerDisconnected(IPeer peer, IDisconnectInfo info)
    {
        try
        {
            int slot = SlotOf(peer.Id);
            if (slot >= 0)
            {
                SetPeer(slot, null);
            }
            FreeSlot(peer.Id);
            RefreshConnectedCount();
            log.LogInformation("Peer {Id} disconnected, slot freed.", peer.Id);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "PeerDisconnected handling failed.");
        }
    }

    /// <summary>Parses a gameplay input packet and updates its player's latch.</summary>
    /// <param name="peer">The sending peer.</param>
    /// <param name="reader">The packet reader to recycle after processing.</param>
    /// <param name="channel">The transport channel.</param>
    /// <param name="delivery">The packet delivery method.</param>
    private void OnReceive(IPeer peer, IReader reader, byte channel, DeliveryMethod delivery)
    {
        try
        {
            if (channel != NetChannel.Gameplay)
            {
                return;
            }
            if (reader.AvailableBytes < 1)
            {
                return;
            }
            if (reader.GetByte() != PacketKind.Input)
            {
                return;
            }
            if (!InputCommand.TryReadBody(reader, out InputCommand cmd))
            {
                return;
            }
            int slot = SlotOf(peer.Id);
            if (slot < 0)
            {
                return;
            }
            cmd.MoveAxis = MatchRules.ClampAxis(cmd.MoveAxis);
            if (!float.IsFinite(cmd.MoveAxis))
            {
                cmd.MoveAxis = 0f;
            }
            LatchInput(slot, cmd);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Input parse failed.");
        }
        finally
        {
            reader.Recycle();
        }
    }

    /// <summary>Resends initial state and periodically broadcasts snapshots.</summary>
    public void FixedUpdate()
    {
        ResendFullStates();
        var matches = world.Where(out MatchAspect aspect);
        if (matches.Count == 0) return;
        ref MatchState match = ref aspect.States.Get(matches[0]);
        if (match.BroadcastCountdown > 0)
        {
            match.BroadcastCountdown--;
            return;
        }
        match.BroadcastCountdown = 2;
        BroadcastSnapshot();
    }

    /// <summary>Retries full-state delivery while new peers complete their handshake.</summary>
    private void ResendFullStates()
    {
        var peerPool = world.GetPool<ServerPeerState>();
        var players = world.Where(out PlayerAspect aspect);
        for (int i = 0; i < players.Count; i++)
        {
            int entity = players[i];
            Player p = aspect.Players.Read(entity);
            if (p.PeerId < 0 || !peerPool.Has(entity)) continue;
            ref ServerPeerState state = ref peerPool.Get(entity);
            if (state.FullStateResend > 0 && state.Peer is { } peer)
            {
                state.FullStateResend--;
                SendFullState(peer, p.Index);
            }
        }
    }

    /// <summary>Stores or clears the transport peer on its player entity.</summary>
    /// <param name="slot">The assigned player slot.</param>
    /// <param name="peer">The connected peer, or null on disconnect.</param>
    private void SetPeer(int slot, IPeer? peer)
    {
        var peers = world.GetPool<ServerPeerState>();
        var players = world.Where(out PlayerAspect aspect);
        for (int i = 0; i < players.Count; i++)
        {
            int entity = players[i];
            if (aspect.Players.Read(entity).Index != slot) continue;
            if (peer is null)
            {
                peers.TryDel(entity);
            }
            else
            {
                peers.TryAddOrGet(entity) = new ServerPeerState { Peer = peer, FullStateResend = 120 };
            }
            return;
        }
    }

    /// <summary>Accumulates a validated input command for one player slot.</summary>
    /// <param name="slot">The assigned player slot.</param>
    /// <param name="cmd">The validated command.</param>
    private void LatchInput(int slot, InputCommand cmd)
    {
        var latchPool = world.GetPool<InputLatch>();
        var players = world.Where(out PlayerAspect aspect);
        for (int i = 0; i < players.Count; i++)
        {
            int e = players[i];
            ref Player p = ref aspect.Players.Get(e);
            if (p.Index != slot)
            {
                continue;
            }
            if (!latchPool.Has(e))
            {
                latchPool.Add(e) = new InputLatch { Sequence = -1 };
            }
            ref InputLatch latch = ref latchPool.Get(e);
            MatchRules.AccumulateLatch(ref latch, cmd);
            return;
        }
    }

    /// <summary>Finds or assigns a player slot for a peer.</summary>
    /// <param name="peerId">The transport peer identifier.</param>
    /// <returns>The assigned slot, or -1 when both slots are occupied.</returns>
    private int BindSlot(int peerId)
    {
        var players = world.Where(out PlayerAspect aspect);
        for (int i = 0; i < players.Count; i++)
        {
            ref Player p = ref aspect.Players.Get(players[i]);
            if (p.PeerId == peerId)
            {
                return p.Index;
            }
        }
        for (int i = 0; i < players.Count; i++)
        {
            ref Player p = ref aspect.Players.Get(players[i]);
            if (p.PeerId < 0)
            {
                p.PeerId = peerId;
                return p.Index;
            }
        }
        return -1;
    }

    /// <summary>Clears a peer assignment and any held input.</summary>
    /// <param name="peerId">The transport peer identifier.</param>
    private void FreeSlot(int peerId)
    {
        var latchPool = world.GetPool<InputLatch>();
        var players = world.Where(out PlayerAspect aspect);
        for (int i = 0; i < players.Count; i++)
        {
            int e = players[i];
            ref Player p = ref aspect.Players.Get(e);
            if (p.PeerId == peerId)
            {
                p.PeerId = -1;
                if (latchPool.Has(e))
                {
                    latchPool.Get(e) = new InputLatch { Sequence = -1 };
                }
            }
        }
    }

    /// <summary>Finds the player slot assigned to a peer.</summary>
    /// <param name="peerId">The transport peer identifier.</param>
    /// <returns>The assigned slot, or -1 when the peer is unbound.</returns>
    private int SlotOf(int peerId)
    {
        var players = world.Where(out PlayerAspect aspect);
        for (int i = 0; i < players.Count; i++)
        {
            Player p = aspect.Players.Read(players[i]);
            if (p.PeerId == peerId)
            {
                return p.Index;
            }
        }
        return -1;
    }

    /// <summary>Updates the match's connected-peer count from player bindings.</summary>
    private void RefreshConnectedCount()
    {
        int bound = CountBound();
        var matches = world.Where(out MatchAspect aspect);
        for (int i = 0; i < matches.Count; i++)
        {
            ref MatchState m = ref aspect.States.Get(matches[i]);
            m.ConnectedPeers = bound;
        }
    }

    /// <summary>Counts player slots currently bound to peers.</summary>
    /// <returns>The number of connected players.</returns>
    private int CountBound()
    {
        int bound = 0;
        var players = world.Where(out PlayerAspect aspect);
        for (int i = 0; i < players.Count; i++)
        {
            if (aspect.Players.Read(players[i]).PeerId >= 0)
            {
                bound++;
            }
        }
        return bound;
    }

    /// <summary>Starts the match countdown when both player slots are occupied.</summary>
    private void MaybeStartCountdown()
    {
        if (CountBound() < MatchRules.MaxPlayers)
        {
            return;
        }
        var matches = world.Where(out MatchAspect aspect);
        for (int i = 0; i < matches.Count; i++)
        {
            ref MatchState m = ref aspect.States.Get(matches[i]);
            if (m.Phase == MatchPhase.WaitingForPlayers)
            {
                m.Phase = MatchPhase.Countdown;
                m.TimeLeft = m.CountdownDuration;
                int eventEntity = eventWorld.NewEntity();
                eventWorld.GetPool<MatchEvent>().Add(eventEntity) = new MatchEvent { Kind = MatchEventKind.MatchStart };
            }
        }
    }

    /// <summary>Sends a snapshot and full-state event to a newly bound peer.</summary>
    /// <param name="peer">The recipient peer.</param>
    /// <param name="slot">The peer's assigned player slot.</param>
    private void SendFullState(IPeer peer, int slot)
    {
        try
        {
            IWriter writer = net.CreateWriter();
            writer.Put(PacketKind.Snapshot);
            snapshots.WriteSnapshot(world, writer, ReadOnlySpan<int>.Empty);
            peer.Send(writer, MatchEvent.Delivery);

            int tick = 0;
            var matches = world.Where(out MatchAspect aspect);
            if (matches.Count > 0) tick = aspect.States.Read(matches[0]).Tick;
            var ev = new MatchEvent { Kind = MatchEventKind.FullState, PlayerIndex = slot, IntArg = tick };
            IWriter eventWriter = net.CreateWriter();
            ev.Write(eventWriter);
            peer.Send(eventWriter, MatchEvent.Delivery);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "FullState send failed.");
        }
    }

    /// <summary>Broadcasts the latest replicated state to all connected peers.</summary>
    private void BroadcastSnapshot()
    {
        try
        {
            if (CountBound() == 0) return;
            IWriter writer = net.CreateWriter();
            writer.Put(PacketKind.Snapshot);
            snapshots.WriteSnapshot(world, writer, ReadOnlySpan<int>.Empty);
            net.SendToAll(writer, DeliveryMethod.Sequenced);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Snapshot broadcast failed.");
        }
    }

    /// <summary>Selects player identity components.</summary>
    private sealed class PlayerAspect : EcsAspect
    {
        public EcsPool<Player> Players = Inc;
    }

    /// <summary>Selects replicated match state.</summary>
    private sealed class MatchAspect : EcsAspect
    {
        public EcsPool<MatchState> States = Inc;
    }
}

/// <summary>Broadcasts queued match events over reliable transport.</summary>
/// <param name="net">The engine network manager.</param>
/// <param name="log">The logger for send failures.</param>
public sealed class ServerMatchEventSystem(INetworkManager net, ILogger<ServerMatchEventSystem> log) : ISystem, IEcsRunOnEvents<MatchEvent>
{
    /// <summary>Sends each queued match event to connected peers.</summary>
    /// <param name="events">The events emitted during this frame.</param>
    public void RunOnEvents(Span<MatchEvent> events)
    {
        for (int i = 0; i < events.Length; i++)
        {
            try
            {
                IWriter writer = net.CreateWriter();
                events[i].Write(writer);
                net.SendToAll(writer, MatchEvent.Delivery);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Reliable event send failed.");
            }
        }
    }
}
