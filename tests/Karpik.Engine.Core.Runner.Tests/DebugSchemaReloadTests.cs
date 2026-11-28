#if DEBUG
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Xunit;

public sealed class DebugSchemaReloadTests
{
    [Fact]
    public async Task AdminReloadWaitsForBothPreparedProofsAndBothBaselines()
    {
        using var f = new Cohort();
        f.B.Host.HoldReady = true;
        f.B.HoldReceive = true;
        var request = f.Server.Reload.RequestAsync();
        f.PumpUntil(() => f.B.HasPacket);
        Assert.False(request.IsCompleted);
        f.B.HoldReceive = false;
        f.PumpUntil(() => f.B.Host.PrepareCount == 1);
        Assert.False(request.IsCompleted);
        Assert.All(f.Nodes, n => Assert.Equal(0, n.Host.CommitCount));
        f.B.Network.HoldBaseline = true;
        f.B.Host.MarkReady();
        f.PumpUntil(() => f.A.Baseline.AppliedRevision == 2 && f.B.Host.CommitCount == 1);
        Assert.Equal(0, f.B.Baseline.AppliedRevision);
        Assert.False(f.Server.Network.GameplaySendingEnabled);
        Assert.False(request.IsCompleted);
        f.B.Network.HoldBaseline = false;
        f.PumpUntil(() => request.IsCompleted);
        Assert.Equal(SimulationCodeReloadStatus.Success, await request);
        Assert.All(f.Nodes, n => { Assert.True(n.Network.GameplaySendingEnabled); Assert.Equal(2, n.Network.GameplayRevision); });
        Assert.Equal(2, f.B.Baseline.AppliedRevision);
        Assert.Same(f.A.ServerPeer, f.A.Network.FirstPeer);
        var second = f.Server.Reload.RequestAsync();
        f.PumpUntil(() => f.B.Host.PrepareCount == 2);
        f.B.Host.MarkReady();
        f.PumpUntil(() => second.IsCompleted);
        Assert.Equal(SimulationCodeReloadStatus.Success, await second);
    }

    [Theory]
    [InlineData("approval")]
    [InlineData("prepared-proof")]
    [InlineData("compiled-schema")]
    [InlineData("disconnect-before")]
    [InlineData("cancel-before")]
    public async Task AnyPreDecisionRejectionAbortsWholeCohort(string reason)
    {
        using var f = new Cohort(mismatchedCompiled: reason == "compiled-schema");
        using var cancel = new CancellationTokenSource();
        f.B.Admit = reason != "approval";
        if (reason == "prepared-proof") f.B.Host.Proof[0] = 42;
        f.B.Host.HoldReady = reason.EndsWith("-before", StringComparison.Ordinal);
        var request = f.Server.Reload.RequestAsync(cancel.Token);
        if (f.B.Host.HoldReady)
        {
            f.PumpUntil(() => f.B.Host.PrepareCount == 1);
            if (reason == "disconnect-before") f.DisconnectB();
            else cancel.Cancel();
        }
        f.PumpUntil(() => request.IsCompleted && f.A.Network.GameplaySendingEnabled);
        Assert.Equal(reason == "cancel-before" ? SimulationCodeReloadStatus.Cancelled : SimulationCodeReloadStatus.Rejected, await request);
        Assert.All(f.Nodes, n => { Assert.Equal(1, n.Network.GameplayRevision); Assert.Equal(0, n.Host.CommitCount); });
        Assert.True(f.Server.Host.IsRunning);
    }

    [Theory]
    [InlineData(1024, 0, true)]
    [InlineData(1025, 0, false)]
    [InlineData(1, 65487, true)] // 36 header + 12 identity + 1 compiled + 65487 mod = 65536
    [InlineData(1, 65488, false)]
    public async Task PreparedWorstRowBudgetRejectsBeforeAnyCommit(int rows, int payloadBytes, bool allowed)
    {
        using var f = new Cohort();
        f.Server.MaximumAdditionalRows = rows;
        foreach (var node in f.Nodes) node.Host.ModRowBytes = payloadBytes;
        var request = f.Server.Reload.RequestAsync();
        f.PumpUntil(() => request.IsCompleted && f.A.Network.GameplaySendingEnabled && f.B.Network.GameplaySendingEnabled);
        Assert.Equal(allowed ? SimulationCodeReloadStatus.Success : SimulationCodeReloadStatus.Rejected, await request);
        Assert.All(f.Nodes, n =>
        {
            Assert.Equal(allowed ? 1 : 0, n.Host.CommitCount);
            Assert.Equal(allowed ? 2 : 1, n.Network.GameplayRevision);
            Assert.True(n.Host.IsRunning);
        });
    }

    [Theory]
    [InlineData(1024, 0, true)]
    [InlineData(1025, 0, false)]
    [InlineData(2, 1, false)] // duplicate identity
    [InlineData(1, 2, false)] // invalid incarnation
    public async Task ClientChecksExistingOwnedStreamBeforeReady(int count, int invalidIdentity, bool allowed)
    {
        using var f = new Cohort();
        Assert.False(f.A.HasReplicaType);
        var existing = f.B.SeedReplicas(1, count, invalidIdentity);
        // Another stream is not subject to this transition's index or reconciliation.
        var foreign = f.B.SeedReplicas(2, 1, invalidIdentity: 2);
        var request = f.Server.Reload.RequestAsync();
        f.PumpUntil(() => request.IsCompleted && f.Nodes.All(n => n.Host.CanCapture || !n.Host.IsRunning));
        Assert.Equal(allowed ? SimulationCodeReloadStatus.Success : SimulationCodeReloadStatus.Rejected, await request);
        Assert.All(f.Nodes, n =>
        {
            Assert.Equal(allowed ? 1 : 0, n.Host.CommitCount);
            Assert.Equal(allowed ? 2 : 1, n.Network.GameplayRevision);
            Assert.True(n.Host.IsRunning && n.Host.CanCapture);
        });
        Assert.All(existing, row => Assert.Equal(!allowed, row.IsAlive));
        Assert.True(Assert.Single(foreign).IsAlive);
        Assert.False(f.A.HasReplicaType); // Empty stream preflight did not even register the component type.
    }

    [Fact]
    public async Task DisconnectAfterReadyQueuedButBeforeCommitDecisionAbortsSurvivors()
    {
        using var f = new Cohort();
        f.B.Host.HoldReady = true;
        var request = f.Server.Reload.RequestAsync();
        f.PumpUntil(() => f.B.Host.PrepareCount == 1);
        f.B.Host.MarkReady();
        f.B.Reload.Advance(); // Ready is already queued for the server, but not observed yet.
        f.DisconnectB();
        f.PumpUntil(() => request.IsCompleted && f.A.Network.GameplaySendingEnabled);
        Assert.Equal(SimulationCodeReloadStatus.Rejected, await request);
        Assert.Equal(0, f.Server.Host.CommitCount);
        Assert.Equal(0, f.A.Host.CommitCount);
        Assert.True(f.A.Host.IsRunning);
        Assert.False(f.B.Host.IsRunning); // Ready + loss, but B never received a decision.
    }

    [Theory]
    [InlineData("cleanup", false)]
    [InlineData("ack-send", false)]
    [InlineData("await-restore", false)]
    [InlineData("cleanup", true)]
    [InlineData("send-failure-without-disconnect", false)]
    public async Task ReadyClientSettlesKnownAbortWithoutRestoreOnlyOnWireLoss(string at, bool previousCommit)
    {
        using var f = new Cohort();
        if (previousCommit)
        {
            var initial = f.Server.Reload.RequestAsync();
            f.PumpUntil(() => initial.IsCompleted);
            Assert.Equal(SimulationCodeReloadStatus.Success, await initial);
        }
        f.A.Network.Sent.Clear();
        var oldEntity = f.A.SeedLocalEntity();
        long revision = f.A.Network.GameplayRevision;
        using var cancel = new CancellationTokenSource();
        f.A.Host.HoldAbortCleanup = true;
        f.B.Host.HoldReady = true;
        var request = f.Server.Reload.RequestAsync(cancel.Token);
        f.PumpUntil(() => f.B.Host.PrepareCount == (previousCommit ? 2 : 1));
        const byte ready = 37, aborted = 42;
        Assert.Contains(ready, f.A.Network.Sent);
        Assert.False(f.A.Host.CanCapture);
        cancel.Cancel();
        f.Server.Reload.Advance();
        f.A.Network.PollEvents(); // Definitive Abort arrives while the connection is still healthy.
        Assert.True(f.A.Host.AbortRequested);

        if (at == "cleanup")
        {
            f.A.ServerPeer!.Disconnect();
            f.A.Reload.Advance();
            Assert.True(f.A.Host.IsRunning);
            Assert.False(f.A.Host.CanCapture);
            Assert.False(f.A.Network.GameplayReceivingEnabled);
        }
        else if (at is "ack-send" or "send-failure-without-disconnect")
        {
            f.A.Network.RefuseSend = (peer, kind) =>
            {
                if (kind != aborted) return false;
                if (at == "ack-send") ((Peer)peer).Disconnect(); // Lost inside TrySend, after the connected check.
                return true;
            };
        }
        f.A.Host.CompleteAbortCleanup();
        f.A.Reload.Advance();
        if (at == "await-restore")
        {
            Assert.Contains(aborted, f.A.Network.Sent);
            Assert.False(f.A.Host.CanCapture);
            f.A.ServerPeer!.Disconnect(); // Server has not polled Aborted and cannot send Restore.
        }
        f.A.Reload.Advance();
        f.A.Reload.Advance(); // Also exercise idle handling after settling a previously advanced revision.
        bool shouldRecover = at != "send-failure-without-disconnect";
        Assert.Equal(shouldRecover, f.A.Host.IsRunning);
        Assert.Equal(shouldRecover, f.A.Host.CanCapture);
        Assert.Equal(shouldRecover, f.A.Network.GameplaySendingEnabled);
        Assert.Equal(shouldRecover, f.A.Network.GameplayReceivingEnabled);
        if (shouldRecover) Assert.True(f.A.Network.Reserved); // Old world may run, but the ended session cannot admit a replacement.
        Assert.Equal(revision, f.A.Network.GameplayRevision);
        Assert.Equal(previousCommit ? 1 : 0, f.A.Host.CommitCount);
        Assert.True(oldEntity.IsAlive);
        if (!shouldRecover) f.A.ServerPeer!.Disconnect(); // Model transport teardown after fail-stop for the server.
        f.PumpUntil(() => request.IsCompleted);
        Assert.Equal(SimulationCodeReloadStatus.Cancelled, await request);
    }

    [Fact]
    public async Task DisconnectAfterDecisionFailStopsAllRemainingParticipants()
    {
        using var f = new Cohort();
        var request = f.Server.Reload.RequestAsync();
        f.PumpUntil(() => f.Server.Host.CommitCount != 0);
        f.DisconnectB();
        f.PumpUntil(() => request.IsCompleted && !f.A.Host.IsRunning);
        Assert.Equal(SimulationCodeReloadStatus.Faulted, await request);
        Assert.False(f.Server.Host.IsRunning);
        Assert.False(f.A.Network.GameplaySendingEnabled);
    }

    [Fact]
    public async Task FailureSendingFinalSettlementToOneClientStillFailStopsTheOther()
    {
        using var f = new Cohort();
        const byte settledFrame = 46;
        f.Server.Network.RefuseSend = (peer, kind) => kind == settledFrame && peer.Id == 2;
        var request = f.Server.Reload.RequestAsync();
        f.PumpUntil(() => request.IsCompleted);
        Assert.Equal(SimulationCodeReloadStatus.Faulted, await request);
        Assert.All(f.Nodes, node => Assert.False(node.Host.IsRunning));
    }

    [Fact]
    public async Task NoClientEntryNoUnauthorizedEntryAndNoLegacyExclusion()
    {
        using var f = new Cohort();
        Assert.Equal(SimulationCodeReloadStatus.Unsupported, await f.A.Reload.RequestAsync());
        f.Server.Admin = false;
        Assert.Equal(SimulationCodeReloadStatus.Unsupported, await f.Server.Reload.RequestAsync());
        f.Server.Admin = true;
        f.Server.Network.Supported = false;
        Assert.Equal(SimulationCodeReloadStatus.Unsupported, await f.Server.Reload.RequestAsync());
        Assert.All(f.Nodes, n => Assert.Equal(0, n.Host.PrepareCount));
    }

    private sealed class Cohort : IDisposable
    {
        internal readonly Node Server, A, B;
        internal Node[] Nodes => [Server, A, B];
        internal Cohort(bool mismatchedCompiled = false)
        {
            Server = new(Side.Server); A = new(Side.Client); B = new(Side.Client, mismatchedCompiled ? 11 : 10);
            Join(A); Join(B);
        }
        private void Join(Node client)
        {
            var atServer = new Peer(Server.Network.Peers.Count + 1);
            var atClient = new Peer(1);
            atServer.Target = client; atServer.Remote = atClient;
            atClient.Target = Server; atClient.Remote = atServer;
            Server.Network.Peers.Add(atServer);
            client.Network.Peers.Add(atClient);
            client.ServerPeer = atClient;
        }
        internal void DisconnectB() => B.ServerPeer!.Disconnect();
        internal void PumpUntil(Func<bool> done)
        {
            for (int i = 0; i < 128 && !done(); i++)
                foreach (var n in Nodes) { if (!n.HoldReceive) n.Network.PollEvents(); n.Reload.Advance(); }
            Assert.True(done(), "The deterministic cohort did not settle.");
        }
        public void Dispose() { foreach (var n in Nodes) n.Dispose(); }
    }

    private sealed class Node : IDisposable
    {
        private readonly EcsDefaultWorld _world = new();
        internal readonly Net Network = new();
        internal readonly FakeHost Host;
        internal readonly RevisionReplicaBaseline Baseline;
        internal readonly DebugSchemaReload Reload;
        internal bool Admit = true, Admin = true, HoldReceive;
        internal int MaximumAdditionalRows;
        internal bool HasPacket => Network.Inbox.Count != 0;
        internal bool HasReplicaType => _world.IsComponentTypeDeclared<NetworkReplica>();
        internal Peer? ServerPeer;
        internal Node(Side side, long schema = 10)
        {
            Host = new(side);
            var compiled = new Projection(schema);
            Baseline = new(_world, 1, compiled);
            Reload = new(Network, Host, (_, _) => Admit, () => true, (_, revision) =>
            {
                if (side == Side.Server) Assert.True(Baseline.TryCapture([], revision));
                return Baseline;
            }, () => { }, compiled, _ => new(_world, default, MaximumAdditionalRows), TimeSpan.FromSeconds(60), () => Admin);
        }
        internal entlong SeedLocalEntity()
        {
            int entity = _world.NewEntity();
            _world.GetPool<LocalTag>().Add(entity);
            return _world.GetEntityLong(entity);
        }
        internal entlong[] SeedReplicas(long stream, int count, int invalidIdentity)
        {
            var pool = _world.GetPool<NetworkReplica>();
            var rows = new entlong[count];
            for (int i = 0; i < count; i++)
            {
                int entity = _world.NewEntity();
                pool.Add(entity) = new NetworkReplica { Stream = stream, Id = invalidIdentity == 1 ? 0 : i,
                    Incarnation = invalidIdentity == 2 ? 0 : 1 };
                rows[i] = _world.GetEntityLong(entity);
            }
            return rows;
        }
        private struct LocalTag : IEcsComponent;
        public void Dispose() { Reload.Dispose(); _world.Destroy(); }
    }

    private sealed class FakeHost(Side side) : ISimulationSchemaReloadHost
    {
        private Local? _local;
        internal byte[] Proof = new byte[32];
        internal bool HoldReady, HoldAbortCleanup, AbortRequested;
        internal bool CanCapture => IsRunning && _local is null;
        internal void CompleteAbortCleanup() => _local!.Done.SetResult(new(SimulationCodeReloadStatus.Cancelled));
        internal int ModRowBytes;
        internal int PrepareCount, CommitCount;
        public Side Side => side;
        public bool IsRunning { get; private set; } = true;
        public SimulationCodeReloadStatus TryReserve(long id, long basis, long target, out ISimulationSchemaReload? reload)
        { reload = _local = new(this); return SimulationCodeReloadStatus.Success; }
        public void FailStop() { IsRunning = false; _local?.Done.TrySetResult(new(SimulationCodeReloadStatus.Faulted)); }
        internal void MarkReady() => _local!.Prepared.TrySetResult(new(SimulationCodeReloadStatus.Success));
        private sealed class Local(FakeHost host) : ISimulationSchemaReload
        {
            internal readonly TaskCompletionSource<SimulationCodeReloadResult> Prepared = new(), Done = new();
            private readonly TaskCompletionSource<SimulationCodeReloadResult> _committed = new();
            public Task<SimulationCodeReloadResult> Ready => Prepared.Task;
            public Task<SimulationCodeReloadResult> Committed => _committed.Task;
            public Task<SimulationCodeReloadResult> Completion => Done.Task;
            public ReadOnlyMemory<byte> SharedSchemaProof => host.Proof;
            public int? MaximumModReplicaRowBytes => host.ModRowBytes;
            public bool TryPrepare(Func<string, bool> admit)
            {
                host.PrepareCount++;
                if (!admit("known-local-artifact")) Prepared.SetResult(new(SimulationCodeReloadStatus.Rejected));
                else if (!host.HoldReady) host.MarkReady();
                return true;
            }
            public bool TryCommit() { host.CommitCount++; return _committed.TrySetResult(new(SimulationCodeReloadStatus.Success)); }
            public bool TryAbort()
            {
                host.AbortRequested = true;
                return host.HoldAbortCleanup || Done.TrySetResult(new(SimulationCodeReloadStatus.Cancelled));
            }
            public bool TryResume() => Done.TrySetResult(new(SimulationCodeReloadStatus.Success));
            public void Release() { if (Done.Task.IsCompleted) host._local = null; }
        }
    }

    private sealed class Peer(int id) : IPeer
    {
        public int Id => id;
        public ConnectionState ConnectionState { get; private set; } = ConnectionState.Connected;
        internal Node Target = null!;
        internal Peer Remote = null!;
        public void Send(IWriter writer, DeliveryMethod deliveryMethod) => throw new NotSupportedException();
        internal void Disconnect() => ConnectionState = Remote.ConnectionState = ConnectionState.Disconnected;
    }

    private sealed class Net : IRevisionTransitionNetwork
    {
        internal readonly List<Peer> Peers = [];
        internal readonly List<byte> Sent = [];
        internal readonly Queue<(Peer Peer, byte Kind, long Op, long Revision, byte[] Payload)> Inbox = new();
        internal bool Supported = true, Reserved, HoldBaseline;
        internal Func<IPeer, byte, bool>? RefuseSend;
        public event IRevisionTransitionNetwork.RevisionControlHandler? RevisionControlReceived;
        public event INetworkManager.NetworkEventHandler NetworkReceiveEvent { add { } remove { } }
        public event INetworkManager.PeerConnectionEventHandler PeerConnectedEvent { add { } remove { } }
        public event INetworkManager.PeerDisconnectionEventHandler PeerDisconnectedEvent { add { } remove { } }
        public event INetworkManager.ConnectionRequestEventHandler ConnectionRequestEvent { add { } remove { } }
        public long GameplayRevision { get; private set; } = 1;
        public bool GameplaySendingEnabled { get; private set; } = true;
        public bool GameplayReceivingEnabled { get; private set; } = true;
        public IPeer? FirstPeer => Peers.Count == 0 ? null : Peers[0];
        public bool CanBeginRevisionTransition => Supported && GameplaySendingEnabled && GameplayReceivingEnabled;
        public void ConfigureRevisionTransitions(long initialRevision) => GameplayRevision = initialRevision;
        public bool TryReserveRevisionCohort(Span<IPeer> peers, out int count)
        {
            count = 0;
            if (Reserved || !CanBeginRevisionTransition || Peers.Count > peers.Length) return false;
            foreach (var p in Peers) if (p.ConnectionState == ConnectionState.Connected) peers[count++] = p;
            return Reserved = count > 0;
        }
        public void ReleaseRevisionCohort() => Reserved = false;
        public bool TrySetGameplayGates(bool send, bool receive)
        { GameplaySendingEnabled = send; GameplayReceivingEnabled = receive; return true; }
        public bool TryAdvanceGameplayRevision(long basis, long target)
        {
            if (GameplaySendingEnabled || GameplayReceivingEnabled || GameplayRevision != basis || target <= basis) return false;
            GameplayRevision = target; return true;
        }
        public bool TrySendRevisionControl(IPeer peer, byte kind, long op, long revision, ReadOnlySpan<byte> payload)
        {
            var p = (Peer)peer;
            if (p.ConnectionState != ConnectionState.Connected || RefuseSend?.Invoke(peer, kind) == true) return false;
            Sent.Add(kind);
            p.Target.Network.Inbox.Enqueue((p.Remote, kind, op, revision, payload.ToArray()));
            return true;
        }
        public void PollEvents()
        {
            while (Inbox.TryPeek(out var next) && (!HoldBaseline || next.Kind != RevisionGameplayControl.BaselineChunk))
            {
                var f = Inbox.Dequeue();
                RevisionControlReceived?.Invoke(f.Peer, f.Kind, f.Op, f.Revision, f.Payload);
            }
        }
        public void Stop() => TrySetGameplayGates(false, false);
        public void Dispose() => Stop();
        public int GetFreePort() => throw new NotSupportedException();
        public void ConfigureProtocolSchema(long schemaHash) => throw new NotSupportedException();
        public void Start(int port) => throw new NotSupportedException();
        public void Connect(string address, int port, string key) => throw new NotSupportedException();
        public void SendToAll(IWriter writer, DeliveryMethod method) => throw new NotSupportedException();
        public IWriter CreateWriter() => throw new NotSupportedException();
    }

    private sealed class Projection(long hash) : IRevisionReplicaProjection
    {
        public long SchemaHash => hash;
        public int? MaximumRowBytes => 1;
        public void Write(EcsWorld world, int entity, IWriter writer) { }
        public void Validate(IReader reader) { }
        public void Apply(EcsWorld world, int entity, IReader reader) { }
    }
}
#endif
