using System.Diagnostics;
using DCFApixels.DragonECS;
using DCFApixels.DragonECS.Core;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Network.LiteNetLib;
using LiteNetLib;
using LiteNetLib.Utils;
using Xunit;
using DeliveryMethod = Karpik.Engine.Shared.Network.Core.DeliveryMethod;
using WireDelivery = LiteNetLib.DeliveryMethod;

namespace Network.Codegen.Tests;

public sealed class RevisionTransitionNetworkTests
{
    private const long Schema = 1347;
    private const long Initial = 31;
    private const string Key = "revision-test";

    [Fact]
    public void Required_gameplay_and_control_share_order_and_replaceable_stays_separate()
    {
        using var pair = new Pair(true, true);
        var received = new List<(int Value, long Revision, DeliveryMethod Delivery)>();
        pair.Server.NetworkReceiveEvent += (_, reader, channel, delivery) =>
        {
            Assert.Equal(0, channel);
            Assert.Equal((byte)PacketType.Command, reader.GetByte());
            received.Add((reader.GetInt(), reader.GameplayRevision, delivery));
            reader.Recycle();
        };
        if (pair.ServerRevision is { } serverRevision)
            serverRevision.RevisionControlReceived += (_, kind, operation, revision, payload) =>
            {
                Assert.Equal(7, kind);
                Assert.Equal(123, operation);
                Assert.True(payload.SequenceEqual(new byte[] { 12 }));
                received.Add((2, revision, DeliveryMethod.ReliableOrdered));
            };

        pair.Write(1);
        pair.ClientPeer.Send(pair.Writer, DeliveryMethod.ReliableUnordered);
        var sent = pair.ClientRevision?.TrySendRevisionControl(pair.ClientPeer, 7, 123, Initial, [12]) ?? false;
        pair.Write(3);
        pair.Client.SendToAll(pair.Writer, DeliveryMethod.ReliableOrdered);
        pair.PumpUntil(() => received.Count >= 2 && received.Any(p => p.Value == 3));

        Assert.Equal((1, Initial, DeliveryMethod.ReliableOrdered), received[0]);
        Assert.True(sent);
        Assert.Equal([(1, Initial, DeliveryMethod.ReliableOrdered), (2, Initial, DeliveryMethod.ReliableOrdered),
            (3, Initial, DeliveryMethod.ReliableOrdered)], received);

        pair.Write(4);
        pair.ClientPeer.Send(pair.Writer, DeliveryMethod.ReliableSequenced);
        pair.PumpUntil(() => received.Count == 4);
        Assert.Equal((4, Initial, DeliveryMethod.ReliableSequenced), received[3]);
    }

    [Fact]
    public void One_pump_delivers_control_while_send_and_receive_gameplay_are_gated()
    {
        using var pair = new Pair(true, true);
        var serverValues = new List<int>();
        var clientValues = new List<int>();
        pair.Server.NetworkReceiveEvent += (_, reader, _, _) => ReadValue(reader, serverValues);
        pair.Client.NetworkReceiveEvent += (_, reader, _, _) => ReadValue(reader, clientValues);
        int controls = 0;
        if (pair.ServerRevision is { } serverRevision)
            serverRevision.RevisionControlReceived += (_, _, _, _, _) => controls++;
        if (pair.ClientRevision is { } clientRevision)
            clientRevision.RevisionControlReceived += (_, _, _, _, _) => controls++;

        // Begin draining without stopping inbound mandatory effects.
        var serverClosed = pair.ServerRevision?.TrySetGameplayGates(sendOpen: false, receiveOpen: true) ?? false;
        var serverWriter = pair.Server.CreateWriter();
        Write(serverWriter, 1);
        pair.Server.FirstPeer!.Send(serverWriter, DeliveryMethod.ReliableOrdered);
        pair.AcceptedPeer.Send(serverWriter, DeliveryMethod.ReliableOrdered);
        pair.Server.SendToAll(serverWriter, DeliveryMethod.ReliableOrdered);
        pair.Write(2);
        pair.ClientPeer.Send(pair.Writer, DeliveryMethod.ReliableOrdered);
        pair.PumpUntil(() => serverValues.Count != 0);
        Assert.Equal([2], serverValues);
        Assert.True(serverClosed);
        Assert.Same(pair.AcceptedPeer, pair.Server.FirstPeer);

        Assert.True(serverRevisionOrFail().TrySetGameplayGates(sendOpen: false, receiveOpen: false));
        pair.Write(3);
        pair.ClientPeer.Send(pair.Writer, DeliveryMethod.ReliableOrdered);
        Assert.True(pair.ClientRevision!.TrySendRevisionControl(pair.ClientPeer, 8, 15, Initial, []));
        Assert.True(serverRevisionOrFail().TrySendRevisionControl(pair.AcceptedPeer, 8, 15, Initial, []));
        pair.PumpUntil(() => controls == 2);
        Assert.Empty(clientValues);
        Assert.Equal([2], serverValues);

        Assert.True(serverRevisionOrFail().TrySetGameplayGates(sendOpen: true, receiveOpen: true));
        Write(serverWriter, 4);
        pair.Server.SendToAll(serverWriter, DeliveryMethod.ReliableOrdered);
        pair.PumpUntil(() => clientValues.Count != 0);
        Assert.Equal([4], clientValues);

        IRevisionTransitionNetwork serverRevisionOrFail() => pair.ServerRevision!;
    }

    [Fact]
    public void Paused_pump_reports_physical_disconnect_without_calling_gameplay_lifecycle()
    {
        using var pair = new Pair(true, true);
        int gameplayDisconnects = 0;
        pair.Server.PeerDisconnectedEvent += (_, _) => gameplayDisconnects++;
        var closed = pair.ServerRevision?.TrySetGameplayGates(false, false) ?? false;
        pair.Client.Stop();
        PumpUntil(pair.Server.PollEvents, () => pair.AcceptedPeer.ConnectionState !=
            Karpik.Engine.Shared.Network.Core.ConnectionState.Connected);
        // Drive the queued transport event, not just the library's background connection state.
        pair.Server.PollEvents();
        Assert.Equal(0, gameplayDisconnects);
        Assert.True(closed);
        Assert.True(pair.ServerRevision!.TrySetGameplayGates(true, true));
        PumpUntil(pair.Server.PollEvents, () => gameplayDisconnects == 1);
    }

#if DEBUG
    [Fact]
    public void Reserved_confirmed_peers_block_new_admission_without_closing_gameplay()
    {
        using var pair = new Pair(true, true);
        var network = pair.ServerRevision!;
        Assert.False(network.TryReserveRevisionCohort([], out _));
        IPeer[] cohort = new IPeer[2];
        Assert.True(network.TryReserveRevisionCohort(cohort, out int count));
        Assert.Equal(1, count);
        Assert.Same(pair.AcceptedPeer, cohort[0]);
        Assert.False(network.TryReserveRevisionCohort(cohort, out _));
        Assert.True(network.GameplaySendingEnabled);
        Assert.True(network.GameplayReceivingEnabled);

        var listener = new EventBasedNetListener();
        bool wasRejected = false;
        listener.PeerDisconnectedEvent += (_, info) => wasRejected = info.Reason == DisconnectReason.ConnectionRejected;
        var raw = new NetManager(listener);
        Assert.True(raw.Start());
        try
        {
            raw.Connect("127.0.0.1", pair.Port, Key);
            PumpUntil(() => { pair.Server.PollEvents(); raw.PollEvents(); }, () => wasRejected);
            Assert.Same(pair.AcceptedPeer, pair.Server.FirstPeer);
        }
        finally { raw.Stop(); }

        network.ReleaseRevisionCohort();
        using var joiner = new LiteNetLibNetworkManager();
        joiner.ConfigureProtocolSchema(Schema);
        joiner.ConfigureRevisionTransitions(Initial);
        joiner.Start(0);
        joiner.Connect("127.0.0.1", pair.Port, Key);
        PumpUntil(() => { pair.Server.PollEvents(); joiner.PollEvents(); }, () => joiner.FirstPeer is not null);
        bool reserved = false;
        PumpUntil(() => { pair.Server.PollEvents(); joiner.PollEvents(); },
            () => reserved || (reserved = network.TryReserveRevisionCohort(cohort, out count)));
        Assert.Equal(2, count);
        network.ReleaseRevisionCohort();
    }
#endif

    [Fact]
    public void Late_old_and_early_new_gameplay_never_reach_the_decoder()
    {
        using var pair = new Pair(true, true);
        var payloads = new List<(int Value, long Revision)>();
        pair.Server.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            Assert.Equal((byte)PacketType.Command, reader.GetByte());
            payloads.Add((reader.GetInt(), reader.GameplayRevision));
            reader.Recycle();
        };
        var controls = 0;
        if (pair.ServerRevision is { } serverRevision)
            serverRevision.RevisionControlReceived += (_, _, _, _, _) => controls++;
        var opened = pair.ServerRevision?.TrySetGameplayGates(false, false) ?? false;
        var committed = pair.ServerRevision?.TryAdvanceGameplayRevision(Initial, Initial + 1) ?? false;
        pair.ServerRevision?.TrySetGameplayGates(false, true);

        pair.Write(1);
        pair.ClientPeer.Send(pair.Writer, DeliveryMethod.ReliableOrdered);
        pair.ClientRevision?.TrySendRevisionControl(pair.ClientPeer, 2, 88, Initial + 1, []);
        pair.PumpUntil(() => controls != 0 || payloads.Count != 0);
        Assert.Empty(payloads);
        Assert.True(opened && committed);
        Assert.Equal(1, controls);

        // Open the sender first, but keep the receiver gated until its Resume decision.
        Assert.True(pair.ServerRevision!.TrySetGameplayGates(false, false));
        Assert.True(pair.ClientRevision!.TrySetGameplayGates(false, false));
        Assert.False(pair.ClientRevision.TryAdvanceGameplayRevision(Initial - 1, Initial + 1));
        Assert.False(pair.ClientRevision.TryAdvanceGameplayRevision(Initial, Initial));
        Assert.True(pair.ClientRevision.TryAdvanceGameplayRevision(Initial, Initial + 1));
        Assert.True(pair.ClientRevision.TrySetGameplayGates(true, true));
        pair.Write(2);
        pair.ClientPeer.Send(pair.Writer, DeliveryMethod.ReliableOrdered);
        Assert.True(pair.ClientRevision.TrySendRevisionControl(pair.ClientPeer, 2, 88, Initial + 1, []));
        pair.PumpUntil(() => controls == 2);
        Assert.Empty(payloads);

        Assert.True(pair.ServerRevision.TrySetGameplayGates(true, true));
        Assert.False(pair.ServerRevision.TryAdvanceGameplayRevision(Initial + 1, Initial + 2));
        pair.Write(3);
        pair.ClientPeer.Send(pair.Writer, DeliveryMethod.ReliableOrdered);
        pair.PumpUntil(() => payloads.Count != 0);
        Assert.Equal([(3, Initial + 1)], payloads);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Legacy_peer_keeps_gameplay_without_qualifying_for_transition(bool optInServer, bool optInClient)
    {
        using var pair = new Pair(optInServer, optInClient);
        var values = new List<int>();
        pair.Server.NetworkReceiveEvent += (_, reader, _, delivery) =>
        {
            Assert.Equal(DeliveryMethod.ReliableUnordered, delivery);
            Assert.Equal(0, reader.GameplayRevision);
            ReadValue(reader, values);
        };
        pair.Write(42);
        pair.Client.SendToAll(pair.Writer, DeliveryMethod.ReliableUnordered);
        pair.PumpUntil(() => values.Count != 0);
        Assert.Equal([42], values);

        var optedIn = optInServer ? pair.ServerRevision : pair.ClientRevision;
        var legacy = optInServer ? pair.AcceptedPeer : pair.ClientPeer;
        Assert.NotNull(optedIn);
        Assert.False(optedIn.CanBeginRevisionTransition);
        Assert.False(optedIn.TrySetGameplayGates(false, true));
        Assert.False(optedIn.TrySendRevisionControl(legacy, 1, 5, Initial, []));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unmodified_LiteNetLib_peer_sees_exact_legacy_bytes(bool optIn)
    {
        using var server = new LiteNetLibNetworkManager();
        server.ConfigureProtocolSchema(Schema);
        if (optIn && (object)server is IRevisionTransitionNetwork revision)
            revision.ConfigureRevisionTransitions(Initial);
        server.ConnectionRequestEvent += request => request.AcceptIfKey(Key);
        var listener = new EventBasedNetListener();
        var client = new NetManager(listener);
        var observed = new List<byte[]>();
        listener.PeerConnectedEvent += peer =>
        {
            var handshake = new NetDataWriter();
            handshake.Put((byte)PacketType.Handshake);
            handshake.Put(Schema);
            peer.Send(handshake, WireDelivery.ReliableOrdered);
        };
        listener.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            observed.Add(reader.GetRemainingBytesSpan().ToArray());
            reader.Recycle();
        };
        server.PeerConnectedEvent += peer =>
        {
            var writer = server.CreateWriter();
            Write(writer, 0x11223344);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
        };
        var port = server.GetFreePort();
        server.Start(port);
        Assert.True(client.Start());
        try
        {
            client.Connect("127.0.0.1", port, Key);
            PumpUntil(() => { server.PollEvents(); client.PollEvents(); }, () => observed.Count >= 2);
            Assert.Equal(2, observed.Count);
            Assert.Equal(new byte[] { (byte)PacketType.Handshake, 0x43, 0x05, 0, 0, 0, 0, 0, 0 }, observed[0]);
            Assert.Equal(new byte[] { (byte)PacketType.Command, 0x44, 0x33, 0x22, 0x11 }, observed[1]);
        }
        finally
        {
            client.Stop();
        }
    }

    [Fact]
    public void Control_ingress_discards_malformed_frames_and_cannot_be_reached_before_negotiation()
    {
        using var server = new LiteNetLibNetworkManager();
        server.ConfigureProtocolSchema(Schema);
        var revision = (object)server as IRevisionTransitionNetwork;
        revision?.ConfigureRevisionTransitions(Initial);
        IPeer? accepted = null;
        IPeer? ready = null;
        server.PeerConnectedEvent += peer => ready = peer;
        server.ConnectionRequestEvent += request =>
        {
            accepted = request.AcceptIfKey(Key);
            var gameplay = server.CreateWriter();
            Write(gameplay, 41);
            accepted!.Send(gameplay, DeliveryMethod.ReliableOrdered);
            server.SendToAll(gameplay, DeliveryMethod.ReliableOrdered);
        };
        var controls = new List<long>();
        if (revision is not null)
            revision.RevisionControlReceived += (_, _, operation, _, _) => controls.Add(operation);
        server.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            reader.Recycle();
            Assert.Fail("An upgraded control frame must never reach the gameplay decoder.");
        };

        var listener = new EventBasedNetListener();
        var client = new NetManager(listener);
        NetPeer? peer = null;
        var frames = new List<byte[]>();
        listener.PeerConnectedEvent += connected => peer = connected;
        listener.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            frames.Add(reader.GetRemainingBytesSpan().ToArray());
            reader.Recycle();
        };
        var port = server.GetFreePort();
        server.Start(port);
        Assert.True(client.Start());
        try
        {
            var request = new NetDataWriter();
            request.Put(Key);
            request.Put("KPRKREV1"u8);
            client.Connect("127.0.0.1", port, request);
            PumpUntil(Pump, () => frames.Any(frame => frame[0] == (byte)PacketType.Handshake));
            var handshake = Assert.Single(frames);
            Assert.Equal(17, handshake.Length);
            Assert.NotNull(accepted);
            Assert.Null(ready);
            Assert.Null(server.FirstPeer);
            Assert.False(revision!.CanBeginRevisionTransition);
            Assert.False(revision.TrySendRevisionControl(accepted, 1, 1, Initial, []));
            peer!.Send(handshake, WireDelivery.ReliableOrdered);
            PumpUntil(Pump, () => ready is not null);
            Assert.Same(accepted, ready);
            Assert.True(revision.TrySetGameplayGates(false, false));

            const byte Control = 4;
            peer.Send(new byte[] { Control }, WireDelivery.ReliableOrdered);
            SendControl(0, 1, Initial, 0);
            SendControl(1, 0, Initial, 0);
            SendControl(1, 2, 0, 0);
            SendControl(1, 3, Initial, IRevisionTransitionNetwork.MaxControlPayloadBytes + 1);
            SendControl(1, 4, Initial, 0);
            PumpUntil(Pump, () => controls.Contains(4));
            Assert.Equal([4L], controls);

            void SendControl(byte kind, long operation, long controlRevision, int padding)
            {
                var frame = new NetDataWriter();
                frame.Put(Control);
                frame.Put(kind);
                frame.Put(operation);
                frame.Put(controlRevision);
                frame.Put(new byte[padding]);
                peer.Send(frame, WireDelivery.ReliableOrdered);
            }
        }
        finally
        {
            client.Stop();
        }

        void Pump() { server.PollEvents(); client.PollEvents(); }
    }

    [Fact]
    public void Opt_in_and_control_bounds_are_checked_and_maximum_payload_is_delivered()
    {
        using var pair = new Pair(true, true);
        var revision = pair.ClientRevision;
        Assert.NotNull(revision);
        Assert.Throws<InvalidOperationException>(() => revision.ConfigureRevisionTransitions(Initial));
        Assert.True(revision.CanBeginRevisionTransition);
        Assert.False(revision.TrySendRevisionControl(pair.ClientPeer, 0, 1, Initial, []));
        Assert.False(revision.TrySendRevisionControl(pair.ClientPeer, 7, 0, Initial, []));
        Assert.False(revision.TrySendRevisionControl(pair.ClientPeer, 7, 1, 0, []));
        Assert.False(revision.TrySendRevisionControl(pair.AcceptedPeer, 7, 1, Initial, []));
        Assert.False(revision.TrySendRevisionControl(pair.ClientPeer, 7, 1, Initial, new byte[IRevisionTransitionNetwork.MaxControlPayloadBytes + 1]));
        int count = 0;
        pair.ServerRevision!.RevisionControlReceived += (_, _, _, _, payload) =>
        {
            Assert.Equal(IRevisionTransitionNetwork.MaxControlPayloadBytes, payload.Length);
            count++;
        };
        Assert.True(revision.TrySendRevisionControl(pair.ClientPeer, 7, 1, Initial, new byte[IRevisionTransitionNetwork.MaxControlPayloadBytes]));
        pair.PumpUntil(() => count == 1);
    }

    [Fact]
    public void Live_cutoffs_wait_for_application_and_resume_precedes_a_reliable_effect()
    {
        using var pair = new Pair(true, true);
        var server = pair.ServerRevision!;
        var client = pair.ClientRevision!;
        var clientTarget = pair.ClientPeer;
        var serverTarget = pair.AcceptedPeer;
        var next = Initial + 1;
        const long operation = 257;
        const byte inputCutoff = 16, effectCutoff = 17, resume = 20;
        var serverInput = new Queue<int>();
        var clientEffects = new Queue<(long Revision, int Value)>();
        pair.Server.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            Assert.Equal((byte)PacketType.Command, reader.GetByte());
            serverInput.Enqueue(reader.GetInt());
        };
        pair.Client.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            Assert.Equal((byte)PacketType.Command, reader.GetByte());
            clientEffects.Enqueue((reader.GameplayRevision, reader.GetInt()));
        };
        var serverBarrier = new RevisionDrainBarrier([serverTarget], operation, Initial, inputCutoff);
        var clientBarrier = new RevisionDrainBarrier([clientTarget], operation, Initial, effectCutoff);
        var applied = new RevisionDrainBarrier([serverTarget], operation, next, RevisionGameplayControl.BaselineApplied);
        var sourceWorld = new EcsWorld();
        var clientWorld = new EcsWorld();
        try
        {
            var codec = new CounterProjection();
            var sourceBaseline = new RevisionReplicaBaseline(sourceWorld, 1, codec);
            var clientBaseline = new RevisionReplicaBaseline(clientWorld, 1, codec);
            var transfer = new RevisionBaselineTransfer(clientBaseline, clientTarget, operation, next);
            bool resumed = false;
            int chunks = 0;
            server.RevisionControlReceived += (peer, kind, id, rev, payload) =>
            {
                var barrier = kind == RevisionGameplayControl.BaselineApplied ? applied : serverBarrier;
                Assert.True(barrier.TryObserve(peer, kind, id, rev, payload.Length));
            };
            client.RevisionControlReceived += (peer, kind, id, rev, payload) =>
            {
                switch (kind)
                {
                    case resume:
                        Assert.Equal(operation, id);
                        Assert.Equal(next, rev);
                        Assert.True(transfer.TryAcceptResume(client, peer, kind, id, rev, payload.Length));
                        resumed = true;
                        break;
                    case RevisionGameplayControl.BaselineChunk:
                        Assert.True(transfer.TryReceiveChunk(peer, kind, id, rev, payload));
                        chunks++;
                        break;
                    default:
                        Assert.True(clientBarrier.TryObserve(peer, kind, id, rev, payload.Length));
                        break;
                }
            };
            pair.Write(5);
            clientTarget.Send(pair.Writer, DeliveryMethod.ReliableOrdered);
            Assert.True(RevisionGameplayControl.TrySendInputCutoff(client, clientTarget, operation, Initial));
            pair.Write(999);
            clientTarget.Send(pair.Writer, DeliveryMethod.ReliableOrdered); // not replayed
            Assert.False(serverBarrier.TryConfirmApplicationDrained());
            pair.PumpUntil(() => serverBarrier.AllMarkersObserved);
            Assert.False(serverBarrier.ApplicationDrained);
            Assert.False(RevisionGameplayControl.TrySendEffectCutoff(server, serverBarrier));
            Assert.Equal(5, Assert.Single(serverInput));

            // The application owns completion and must flush its resulting mandatory effects first.
            var writer = server.CreateWriter();
            Write(writer, serverInput.Dequeue() + 1);
            serverTarget.Send(writer, DeliveryMethod.ReliableOrdered);
            Assert.True(serverBarrier.TryConfirmApplicationDrained());
            Assert.True(RevisionGameplayControl.TrySendEffectCutoff(server, serverBarrier));
            pair.PumpUntil(() => clientBarrier.AllMarkersObserved);
            Assert.False(clientBarrier.ApplicationDrained);
            Assert.Equal((Initial, 6), clientEffects.Dequeue()); // app consumes, not a transport ACK
            Assert.True(clientBarrier.TryConfirmApplicationDrained());
            Assert.True(server.TrySetGameplayGates(false, false));
            Assert.True(client.TrySetGameplayGates(false, false));
            Assert.True(server.TryAdvanceGameplayRevision(Initial, next));
            Assert.True(client.TryAdvanceGameplayRevision(Initial, next));

            var visible = new RevisionReplicaIdentity[130];
            for (int i = 0; i < visible.Length; i++)
            {
                int entity = sourceWorld.NewEntity();
                visible[i] = new(entity, i, 1);
                sourceWorld.GetPool<Counter>().Add(entity).Value = i + 100;
            }
            Assert.True(sourceBaseline.TryCapture(visible, next));
            Assert.False(RevisionGameplayControl.TryResumeServer(server, applied));
            Assert.True(RevisionBaselineTransfer.TrySend(server, serverTarget, operation, next, sourceBaseline));
            pair.PumpUntil(() => transfer.Complete);
            Assert.True(chunks > 1);
            Assert.Equal(0, clientWorld.Count);
            Assert.False(RevisionGameplayControl.TryResumeServer(server, applied));
            Assert.True(transfer.TryApplyAndAcknowledge(client, clientEffects.Clear));
            Assert.True(clientBaseline.TryFind(0, 1, out int replicated));
            Assert.Equal(100, clientWorld.GetPool<Counter>().Get(replicated).Value);
            pair.PumpUntil(() => applied.AllMarkersObserved);
            Assert.True(RevisionGameplayControl.TryResumeServer(server, applied));
            Write(writer, 12);
            serverTarget.Send(writer, DeliveryMethod.ReliableOrdered);
            pair.PumpUntil(() => clientEffects.Count == 1);
            Assert.True(resumed);
            Assert.Equal((next, 12), clientEffects.Dequeue()); // first required new-revision app effect
            Assert.Empty(serverInput);
        }
        finally
        {
            sourceWorld.Destroy();
            clientWorld.Destroy();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_replica_preflight_leaves_generations_callbacks_and_pending_deletes_untouched(bool duplicate)
    {
        var world = new EcsWorld();
        try
        {
            var pool = world.GetPool<NetworkReplica>();
            int first = world.NewEntity(), second = world.NewEntity(), pending = world.NewEntity();
            pool.Add(first) = new NetworkReplica { Stream = 1, Id = 1, Incarnation = 1 };
            pool.Add(second) = new NetworkReplica { Stream = 1, Id = duplicate ? 1 : 2, Incarnation = 1 };
            pool.Add(pending) = new NetworkReplica { Stream = 1, Id = -1, Incarnation = 0 };
            world.DelEntity(pending);
            var observer = new ReplicaPoolObserver();
            pool.AddListener(observer);
            short firstGen = world.GetRawEntLong(first).gen, secondGen = world.GetRawEntLong(second).gen;

            var receiver = new RevisionReplicaBaseline(world, 1, new CounterProjection());
            Assert.Equal(!duplicate, receiver.CanReconcileExistingStream());
            Assert.Equal((0, 0, firstGen, secondGen),
                (observer.Gets, observer.Deletes, world.GetRawEntLong(first).gen, world.GetRawEntLong(second).gen));
            Assert.True(pool.Has(pending));
            Assert.NotEqual(firstGen, world.GetGen(first)); // Fixture really started with a sleeping generation.
        }
        finally { world.Destroy(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Baseline_reconciliation_preserves_local_successor_when_delete_callback_reuses_replica_slot(bool keepSecond)
    {
        var world = new EcsWorld();
        try
        {
            var pool = world.GetPool<NetworkReplica>();
            int first = world.NewEntity(), second = world.NewEntity();
            Assert.True(first < second); // The read-only scanner indexes A before B.
            pool.Add(first) = new NetworkReplica { Stream = 1, Id = 1, Incarnation = 1 };
            pool.Add(second) = new NetworkReplica { Stream = 1, Id = 2, Incarnation = 1 };
            var counters = world.GetPool<Counter>();
            counters.Add(second).Value = 99;
            var baseline = new RevisionReplicaBaseline(world, 1, new CounterProjection());
            Assert.True(baseline.TryCapture(keepSecond ? [new(second, 2, 1)] : [], 2));
            var reuse = new ReuseSlotDuringDelete(world, first, second);
            world.AddListener(reuse);

            Assert.True(baseline.TryApply(baseline.Encoded, 2));
            Assert.True(reuse.Ran);
            Assert.True(world.IsUsed(second));
            Assert.False(pool.Has(second));
            Assert.Equal(73, counters.Read(second).Value);
            Assert.Equal(keepSecond, baseline.TryFind(2, 1, out int replica));
            if (keepSecond)
            {
                Assert.NotEqual(second, replica);
                Assert.Equal(99, counters.Read(replica).Value);
            }
        }
        finally { world.Destroy(); }
    }

    private sealed class ReuseSlotDuringDelete(EcsWorld world, int trigger, int reused) : IEcsEntityEventListener
    {
        public bool Ran;
        public void OnNewEntity(int entityID) { }
        public void OnMigrateEntity(int entityID) { }
        public void OnDelEntity(int entityID)
        {
            if (entityID != trigger || Ran) return;
            Ran = true;
            world.DelEntity(reused);
            world.ReleaseDelEntityBufferAll();
            world.GetPool<Counter>().Add(world.NewEntity(reused)).Value = 73;
        }
    }

    private sealed class ReplicaPoolObserver : IEcsPoolEventListener
    {
        public int Gets, Deletes;
        public void OnAdd(int entityID) { }
        public void OnGet(int entityID) => Gets++;
        public void OnDel(int entityID) => Deletes++;
    }

    private struct Counter : IEcsComponent { public int Value; }

    private sealed class CounterProjection : IRevisionReplicaProjection
    {
        public long SchemaHash => 145;
        public void Write(EcsWorld world, int entity, IWriter writer) => writer.Put(world.GetPool<Counter>().Get(entity).Value);
        public void Validate(IReader reader) => _ = reader.GetInt();
        public void Apply(EcsWorld world, int entity, IReader reader) => world.GetPool<Counter>().TryAddOrGet(entity).Value = reader.GetInt();
    }

    private static void ReadValue(IReader reader, List<int> values)
    {
        Assert.Equal((byte)PacketType.Command, reader.GetByte());
        values.Add(reader.GetInt());
        reader.Recycle();
    }

    private static void Write(IWriter writer, int value)
    {
        writer.Reset();
        writer.Put((byte)PacketType.Command);
        writer.Put(value);
    }

    private static void PumpUntil(Action pump, Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            pump();
            Thread.Sleep(1);
        }
        Assert.True(condition(), "Timed out pumping actual LiteNetLib UDP loopback.");
    }

    private sealed class Pair : IDisposable
    {
        public readonly LiteNetLibNetworkManager Server = new();
        public readonly LiteNetLibNetworkManager Client = new();
        public readonly IWriter Writer;
        public int Port { get; }
        public IPeer ClientPeer => Client.FirstPeer!;
        public IPeer AcceptedPeer = null!;
        public IRevisionTransitionNetwork? ServerRevision => (object)Server as IRevisionTransitionNetwork;
        public IRevisionTransitionNetwork? ClientRevision => (object)Client as IRevisionTransitionNetwork;

        public Pair(bool optInServer, bool optInClient)
        {
            // Generated senders can already own a reusable writer when network setup opts in.
            Writer = Client.CreateWriter();
            Server.ConfigureProtocolSchema(Schema);
            Client.ConfigureProtocolSchema(Schema);
            if (optInServer) ServerRevision?.ConfigureRevisionTransitions(Initial);
            if (optInClient) ClientRevision?.ConfigureRevisionTransitions(Initial);
            Server.ConnectionRequestEvent += request => AcceptedPeer = request.AcceptIfKey(Key)!;
            var connected = 0;
            Server.PeerConnectedEvent += _ => connected++;
            Client.PeerConnectedEvent += _ => connected++;
            Port = Server.GetFreePort();
            try
            {
                Server.Start(Port);
                Client.Start(0);
                Client.Connect("127.0.0.1", Port, Key);
                PumpUntil(() => connected == 2);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Write(int value) => RevisionTransitionNetworkTests.Write(Writer, value);
        public void PumpUntil(Func<bool> condition) => RevisionTransitionNetworkTests.PumpUntil(() =>
        {
            Server.PollEvents();
            Client.PollEvents();
        }, condition);

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }
}
