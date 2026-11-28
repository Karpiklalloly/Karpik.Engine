using System.Collections.Concurrent;
using Karpik.Engine.Core;
using Xunit;

public sealed class ModLivePairedSessionTests
{
    [Fact]
    public async Task ReadyMismatchAbortsBothBeforeCommitAndWaitsForCleanup()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pair = new Pair();
        pair.Client.Fingerprint[0] = 1;
        pair.Client.Hold = ModLiveIpcKind.Abort;
        Task<SimulationCodeReloadStatus> apply = Apply(pair.CreateSession(), timeout.Token);
        await pair.Client.Held.Task.WaitAsync(timeout.Token);
        Assert.False(apply.IsCompleted);
        Assert.DoesNotContain(ModLiveIpcKind.Commit, pair.Server.Sent);
        Assert.DoesNotContain(ModLiveIpcKind.Commit, pair.Client.Sent);
        pair.Client.Release();
        Assert.Equal(SimulationCodeReloadStatus.Rejected, await apply.WaitAsync(timeout.Token));
    }

    [Theory]
    [InlineData((int)ModLiveIpcKind.Prepare, (int)ModLiveIpcKind.Abort, SimulationCodeReloadStatus.Rejected)]
    [InlineData((int)ModLiveIpcKind.Commit, (int)ModLiveIpcKind.FailStop, SimulationCodeReloadStatus.Faulted)]
    public async Task LossBeforeDecisionAbortsButLossAfterDecisionFailStops(
        int lossAt, int remainingPeerDecision, SimulationCodeReloadStatus expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pair = new Pair();
        pair.Client.Hold = (ModLiveIpcKind)lossAt;
        Task<SimulationCodeReloadStatus> apply = Apply(pair.CreateSession(), timeout.Token);
        await pair.Client.Held.Task.WaitAsync(timeout.Token);
        pair.Client.Peer.Close();
        Assert.Equal(expected, await apply.WaitAsync(timeout.Token));
        Assert.Contains((ModLiveIpcKind)remainingPeerDecision, pair.Server.Sent);
        Assert.DoesNotContain(ModLiveIpcKind.Resume, pair.Server.Sent);
        Assert.Equal(expected == SimulationCodeReloadStatus.Faulted, pair.Server.Peer.Loss.IsCompleted);
    }

    [Fact]
    public async Task LatePrepareRejectionIsNotAnAbortFailure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pair = new Pair();
        pair.Client.RejectPrepare = pair.Server.RejectPrepare = true;
        Assert.Equal(SimulationCodeReloadStatus.Rejected, await Apply(pair.CreateSession(), timeout.Token));
        Assert.False(pair.Server.Peer.Loss.IsCompleted);
        Assert.False(pair.Client.Peer.Loss.IsCompleted);
        Assert.DoesNotContain(ModLiveIpcKind.FailStop, pair.Client.Sent);
    }

    [Fact]
    public async Task PrepareWithdrawalFromAlreadyReadyPeerWinsBeforeCommitWithBothIpcPipesOpen()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pair = new Pair();
        pair.Client.Hold = ModLiveIpcKind.Prepare;
        pair.Server.WithdrawAfterReady = true;
        var session = pair.CreateSession();
        var apply = Apply(session, deadline.Token);
        await pair.Client.Held.Task.WaitAsync(deadline.Token);
        pair.Client.Release();
        Assert.Equal(SimulationCodeReloadStatus.Rejected, await apply.WaitAsync(deadline.Token));
        Assert.DoesNotContain(ModLiveIpcKind.Commit, pair.Server.Sent);
        Assert.DoesNotContain(ModLiveIpcKind.Commit, pair.Client.Sent);
        Assert.False(pair.Server.Peer.Loss.IsCompleted);
        Assert.False(pair.Client.Peer.Loss.IsCompleted);
        Assert.NotNull(await session.DiscoverAsync(deadline.Token));
        Assert.Equal(ModLiveIpcProtocol.InitialRevision, pair.Server.LastOperation.Basis);
    }

    [Fact]
    public async Task WithoutProofOfNetworkPairingNothingIsSent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pair = new Pair();
        Assert.Equal(SimulationCodeReloadStatus.Unsupported, await Apply(pair.CreateSession(paired: false), timeout.Token));
        Assert.Empty(pair.Server.Sent);
        Assert.Empty(pair.Client.Sent);
    }

    [Fact]
    public async Task SuccessfulApplyWaitsForBothHandoffsAndAdvancesTheAcceptedRevision()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pair = new Pair();
        pair.Client.Hold = ModLiveIpcKind.Resume;
        var session = pair.CreateSession();
        Task<SimulationCodeReloadStatus> apply = Apply(session, timeout.Token);
        await pair.Client.Held.Task.WaitAsync(timeout.Token);
        Assert.Contains(ModLiveIpcKind.Resume, pair.Server.Sent);
        Assert.False(apply.IsCompleted);
        Assert.Equal(SimulationCodeReloadStatus.Busy, await Apply(session, timeout.Token));
        pair.Client.Release();
        Assert.Equal(SimulationCodeReloadStatus.Success, await apply.WaitAsync(timeout.Token));
        var discovered = await session.DiscoverAsync(timeout.Token);
        Assert.Equal(2UL, pair.Client.LastOperation.Basis);
        Assert.Equal(2UL, pair.Client.LastOperation.Target);
        Assert.NotNull(discovered);
        Assert.Equal(["demo"], discovered.Value.Client);
        Assert.Equal(["demo"], discovered.Value.Server);
    }

    private sealed class Pair
    {
        internal readonly Endpoint Server;
        internal readonly Endpoint Client;

        internal Pair()
        {
            var owner = new ModLiveLaunchOwner();
            using var server = owner.CreateAssociation(Side.Server);
            using var client = owner.CreateAssociation(Side.Client);
            Server = new(server.Identity);
            Client = new(client.Identity);
        }

        internal ModLivePairedSession CreateSession(bool paired = true) =>
            new(Server.Peer, Client.Peer, (_, _) => paired, TimeSpan.FromSeconds(5));
    }

    private static Task<SimulationCodeReloadStatus> Apply(ModLivePairedSession session, CancellationToken token) =>
        session.ApplyAsync(["demo"], token);

    private sealed class Endpoint
    {
        internal readonly ModLiveIpcPeer Peer;
        internal readonly ConcurrentQueue<ModLiveIpcKind> Sent = new();
        internal readonly byte[] Fingerprint = new byte[32];
        internal readonly TaskCompletionSource Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ModLiveIpcKind Hold;
        internal bool RejectPrepare, WithdrawAfterReady;
        private ModLiveIpcMessage _waiting;
        internal ModLiveTransition.Operation LastOperation;

        internal Endpoint(ModLiveIpcIdentity identity) => Peer = new(identity, isOwner: true, Send, () => { });

        private Task Send(IpcMessage frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(ModLiveIpcProtocol.TryDecode(frame, fromOwner: true, out var command));
            Sent.Enqueue(command.Kind);
            LastOperation = command.Operation;
            if (command.Kind == Hold)
            {
                _waiting = command;
                Held.TrySetResult();
            }
            else Acknowledge(command);
            return Task.CompletedTask;
        }

        internal void Release() => Acknowledge(_waiting);

        private void Acknowledge(ModLiveIpcMessage command)
        {
            var fact = command.Kind switch
            {
                ModLiveIpcKind.Discover => ModLiveIpcKind.Discovered,
                ModLiveIpcKind.Drain => ModLiveIpcKind.Drained,
                ModLiveIpcKind.Prepare => RejectPrepare ? ModLiveIpcKind.Rejected : ModLiveIpcKind.Ready,
                ModLiveIpcKind.Commit => ModLiveIpcKind.Committed,
                ModLiveIpcKind.Baseline => ModLiveIpcKind.BaselineApplied,
                ModLiveIpcKind.Abort => ModLiveIpcKind.Aborted,
                ModLiveIpcKind.Resume => ModLiveIpcKind.Resumed,
                _ => default
            };
            if (fact != default)
            {
                ReadOnlyMemory<byte> payload = fact == ModLiveIpcKind.Ready ? Fingerprint : default;
                if (fact == ModLiveIpcKind.Discovered)
                {
                    Assert.True(ModLiveIpcSelection.TryEncode(["demo"], out byte[] ids));
                    payload = ids;
                }
                Assert.True(Peer.TryReceive(ModLiveIpcProtocol.Encode(new(fact, command.Operation, payload), fromOwner: false)));
                if (fact == ModLiveIpcKind.Ready && WithdrawAfterReady)
                    Assert.True(Peer.TryReceive(ModLiveIpcProtocol.Encode(new(ModLiveIpcKind.Rejected, command.Operation), fromOwner: false)));
            }
        }
    }
}
