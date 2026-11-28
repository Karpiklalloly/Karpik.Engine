#if DEBUG
using System.Buffers.Binary;
using System.Diagnostics;
using Karpik.Engine.Core;
using Command = Karpik.Engine.Core.ModLiveTransition.Command;
using Fact = Karpik.Engine.Core.ModLiveTransition.Fact;
using Operation = Karpik.Engine.Core.ModLiveTransition.Operation;
using Status = Karpik.Engine.Core.SimulationCodeReloadStatus;
using static Karpik.Engine.Shared.Network.Core.RevisionGameplayControl;

namespace Karpik.Engine.Shared.Network.Core;

/// <summary>Debug-only integration on one serialized network owner. Not an RPC handler or a package-admission service.</summary>
/// <remarks>Poll the existing transport and then Advance, including under the Runner lease. The application callback must
/// report true only after old required callbacks, structural playback and sends finish. The baseline callback runs on the
/// quiescent owner after local commit; server returns a captured visible baseline and client returns a bound empty receiver.
/// Once baseline/abort cleanup is complete, application packet callbacks must be safe even in the same poll as Resume/Restore.</remarks>
public sealed class DebugSchemaReload : IDisposable
{
    private const byte Offer = 32, Accepted = 33, StartDrain = 34, Drained = 35, Prepare = 36, Ready = 37,
        Reject = 38, Commit = 39, Committed = 40, Abort = 41, Aborted = 42, Restore = 43, FailStop = 44,
        Resumed = 45, Settled = 46;
    private const ulong HostId = 64;
    private const int ProofBytes = sizeof(long) + 32;

    private readonly IRevisionTransitionNetwork _network;
    private readonly ISimulationSchemaReloadHost _host;
    private readonly Func<IPeer?, string, bool> _admit;
    private readonly Func<bool> _drained;
    private readonly Func<IPeer?, long, RevisionReplicaBaseline?> _baseline;
    private readonly Action _clearClientCaches;
    private readonly IRevisionReplicaProjection _compiled;
    private readonly Func<IPeer, RevisionBaselineBudget> _budget;
    private readonly int[] _maximumRows = new int[63];
    private long CompiledSchema => _compiled.SchemaHash;
    private readonly TimeSpan _timeout;
    private readonly Func<bool>? _authorize;
    private readonly ModLiveTransition? _transition;
    private readonly IPeer[] _members = new IPeer[63];
    private readonly byte[] _remoteProof = new byte[ProofBytes];
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly bool _server;
    private int _count;
    private long _lastId, _phaseStarted;
    private Operation _operation, _lastSettled;
    private IPeer? _settledServer;
    private Command _command;
    private ISimulationSchemaReload? _local;
    private RevisionDrainBarrier? _drainBarrier, _applied;
    private RevisionBaselineTransfer? _receiveBaseline;
    private TaskCompletionSource<Status>? _request;
    private CancellationToken _cancel;
    private ulong _accepted, _started, _aborted, _resumed;
    private bool _drainedLocal, _prepared, _committed, _decided, _baselineApplied, _resumeReceived, _reportedResume;
    private bool _abortReceived, _abortAcknowledged, _hasRemoteProof, _interrupted, _halt, _failed, _disposed;

    /// <remarks>For a client (null peer), the baseline factory opens a receiver for the same owned stream before Ready
    /// at the basis revision and again after Commit at the target revision. The pre-Ready call must not modify the world.
    /// For a server, the factory captures the target baseline.</remarks>
    public DebugSchemaReload(IRevisionTransitionNetwork network, ISimulationSchemaReloadHost host,
        Func<IPeer?, string, bool> admitLocalArtifact, Func<bool> applicationDrained,
        Func<IPeer?, long, RevisionReplicaBaseline?> baseline, Action clearClientCaches,
        IRevisionReplicaProjection compiled, Func<IPeer, RevisionBaselineBudget> budget, TimeSpan timeout,
        Func<bool>? authorizeServerAdmin = null)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(admitLocalArtifact);
        ArgumentNullException.ThrowIfNull(applicationDrained);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(clearClientCaches);
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(budget);
        if (network.GameplayRevision <= 0 || compiled.SchemaHash == 0 || timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentException("Debug revision control requires configured transport, compiled schema and a bounded duration.");
        _network = network; _host = host; _admit = admitLocalArtifact; _drained = applicationDrained;
        _baseline = baseline; _clearClientCaches = clearClientCaches; _compiled = compiled; _budget = budget;
        _timeout = timeout; _authorize = authorizeServerAdmin;
        _server = host.Side == Side.Server;
        if (_server) _transition = new((ulong)network.GameplayRevision);
        network.RevisionControlReceived += OnControl;
    }

    /// <summary>Only call from a trusted server-admin surface. The task settles after Runner and network handoff.</summary>
    public Task<Status> RequestAsync(CancellationToken token = default)
    {
        RequireOwner();
        if (!_server || _disposed || _authorize?.Invoke() != true) return Task.FromResult(Status.Unsupported);
        if (_failed) return Task.FromResult(Status.Faulted);
        if (_operation.Id != 0) return Task.FromResult(Status.Busy);
        if (token.IsCancellationRequested) return Task.FromResult(Status.Cancelled);
        long basis = _network.GameplayRevision;
        if (basis <= 0 || basis == long.MaxValue || _lastId == long.MaxValue) return Task.FromResult(Status.InvalidState);
        if (!_network.TryReserveRevisionCohort(_members, out _count)) return Task.FromResult(Status.Unsupported);
        var status = _host.TryReserve(++_lastId, basis, basis + 1, out _local);
        if (status != Status.Success)
        {
            Reset();
            return Task.FromResult(status);
        }
        _operation = new((ulong)_lastId, (ulong)basis, (ulong)(basis + 1));
        Span<ModLiveTransition.Participant> cohort = stackalloc ModLiveTransition.Participant[_count + 1];
        cohort[0] = new(HostId, false);
        for (int i = 0; i < _count; i++) cohort[i + 1] = new((ulong)i + 1, true);
        if (_transition!.TryBegin(_operation, cohort) != ModLiveTransition.Admission.Started)
        {
            _local!.TryAbort();
            Reset();
            return Task.FromResult(Status.InvalidState);
        }
        _cancel = token;
        _phaseStarted = Stopwatch.GetTimestamp();
        return (_request = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    }

    public void Advance()
    {
        if (_failed || _disposed) return;
        if (_operation.Id == 0)
        {
            // After a live revision, that connection owns the session. A disconnect can replace a lost FailStop.
            if (_settledServer is { } server && (_halt || server.ConnectionState != ConnectionState.Connected))
            { RequireOwner(); Stop(); }
            return;
        }
        RequireOwner();
        try
        {
            if (_server) AdvanceServer();
            else AdvanceClient();
        }
        catch (Exception) when (!_failed)
        {
            if (_server && !_decided && _command != Command.Abort)
                _transition!.Interrupt(_operation, ModLiveTransition.Interruption.Stop);
            else if (!_server && !_decided && !_abortReceived)
                RejectClient();
            else Stop();
        }
    }

    private void AdvanceServer()
    {
        var transition = _transition!;
        if (_command != Command.Abort && (_interrupted || !_host.IsRunning || _cancel.IsCancellationRequested || Expired() || !AllConnected()))
            transition.Interrupt(_operation, ModLiveTransition.Interruption.Stop);
        Command next = transition.Step().Command;
        if (next != Command.None)
        {
            _command = next;
            switch (next)
            {
                case Command.Drain:
                    _drainBarrier = Barrier(InputCutoff, Basis);
                    SendAll(Offer);
                    break;
                case Command.Prepare:
                    if (!_local!.TryPrepare(digest => _admit(null, digest))) _interrupted = true;
                    else SendAll(Prepare);
                    break;
                case Command.Commit:
                    _decided = true; // Step taking Commit was the one irreversible cohort decision.
                    if (!_local!.TryCommit()) _interrupted = true;
                    else SendAll(Commit);
                    break;
                case Command.Baseline:
                    _applied = Barrier(BaselineApplied, Target);
                    for (int i = 0; i < _count; i++)
                        if (_baseline(_members[i], Target) is not { } captured || !captured.IsWithinReservation(CompiledSchema, _maximumRows[i]) ||
                            !RevisionBaselineTransfer.TrySend(_network, _members[i], Id, Target, captured))
                        { _interrupted = true; return; }
                    break;
                case Command.Resume:
                    if (!TryResumeServer(_network, _applied!) || !_local!.TryResume()) _interrupted = true;
                    break;
                case Command.Abort:
                    _phaseStarted = Stopwatch.GetTimestamp(); // recovery has its own budget, never the cancelled request
                    if (!_local!.TryAbort()) { Stop(); return; }
                    SendAll(Abort, surviving: true);
                    break;
                case Command.FailStop:
                    Stop();
                    return;
            }
        }

        if (_interrupted && _command != Command.Abort) return;
        switch (_command)
        {
            case Command.Drain when _accepted == All && _started == 0:
                for (int i = 0; i < _count; i++)
                {
                    if (!Send(_members[i], StartDrain)) { _interrupted = true; return; }
                    _started |= 1UL << i;
                }
                break;
            case Command.Drain when !_drainedLocal && _drainBarrier!.AllMarkersObserved && _drained():
                _drainBarrier.TryConfirmApplicationDrained();
                if (!TrySendEffectCutoff(_network, _drainBarrier) || !_network.TrySetGameplayGates(false, false)) _interrupted = true;
                else { _drainedLocal = true; transition.Receive(_operation, HostId, Fact.Drained); }
                break;
            case Command.Prepare when !_prepared && _local!.Ready.IsCompleted:
                if (!Succeeded(_local.Ready) || _local.SharedSchemaProof.Length != 32 || (_hasRemoteProof && !ProofMatches(_remoteProof)) ||
                    !TryReserveBaselines(_local.MaximumModReplicaRowBytes))
                    _interrupted = true;
                else { _prepared = true; transition.Receive(_operation, HostId, Fact.Ready); }
                break;
            case Command.Commit when !_committed && _local!.Committed.IsCompleted:
                if (!Succeeded(_local.Committed) || !_network.TryAdvanceGameplayRevision(Basis, Target)) _interrupted = true;
                else { _committed = true; transition.Receive(_operation, HostId, Fact.Committed); }
                break;
            case Command.Resume when _resumed == All && _local!.Completion.IsCompleted:
                if (!Succeeded(_local.Completion)) { _interrupted = true; return; }
                SendAll(Settled);
                if (_interrupted) return;
                if (!transition.Complete(_operation, Command.Resume)) { Stop(); return; }
                Finish(Status.Success);
                break;
            case Command.Abort:
                if (Expired() || FailedCleanup(_local!.Completion)) { Stop(); return; }
                if (!_local.Completion.IsCompleted || (_started & ConnectedMask() & ~_aborted) != 0) return;
                if (!_network.TrySetGameplayGates(_network.GameplaySendingEnabled, true)) { Stop(); return; }
                if (!SendAll(Restore, surviving: true, mask: _started) || !_network.TrySetGameplayGates(true, true)) { Stop(); return; }
                if (!transition.Complete(_operation, Command.Abort)) { Stop(); return; }
                Finish(_cancel.IsCancellationRequested ? Status.Cancelled : Status.Rejected);
                break;
        }
    }

    private bool TryReserveBaselines(int? stagedModRowBytes)
    {
        for (int i = 0; i < _count; i++)
            if (!_budget(_members[i]).TryReserve(_compiled, stagedModRowBytes, out _maximumRows[i])) return false;
        return true;
    }

    private void AdvanceClient()
    {
        IPeer peer = _members[0];
        if (_halt || (_decided && (!_host.IsRunning || Expired() || !AllConnected()))) { Stop(); return; }
        bool connected = peer.ConnectionState == ConnectionState.Connected;
        if (!_decided && !_abortReceived && !connected)
        {
            // After Ready only an explicit Abort rules out the possibility of a lost Commit.
            if (_prepared) { Stop(); return; }
            BeginClientAbort();
            _abortInferred = true;
        }
        if (!_decided && !_abortReceived && !_interrupted && (!_host.IsRunning || Expired())) RejectClient();
        if (_abortReceived)
        {
            if (_halt || FailedCleanup(_local!.Completion) || Expired()) { Stop(); return; }
            if (!_local.Completion.IsCompleted) return;
            if (!connected) { SettleDisconnectedAbort(peer); return; }
            if (!_abortAcknowledged)
            {
                if (!Send(peer, Aborted))
                {
                    if (peer.ConnectionState == ConnectionState.Connected) Stop();
                    else SettleDisconnectedAbort(peer);
                    return;
                }
                _abortAcknowledged = true;
                if (_started == 0) Reset();
            }
            return;
        }
        if (_interrupted) { if (Expired()) Stop(); return; }
        switch (_command)
        {
            case Command.Drain when !_drainedLocal && _drainBarrier?.AllMarkersObserved == true && _drained():
                _drainBarrier.TryConfirmApplicationDrained();
                if (!_network.TrySetGameplayGates(false, false) || !Send(peer, Drained)) RejectClient();
                else _drainedLocal = true;
                break;
            case Command.Prepare when !_prepared:
                if (!_startedPrepare)
                {
                    _startedPrepare = true;
                    if (!_local!.TryPrepare(digest => _admit(peer, digest))) { RejectClient(); return; }
                }
                if (!_local!.Ready.IsCompleted) return;
                if (!Succeeded(_local.Ready) || _local.SharedSchemaProof.Length != 32 || _local.MaximumModReplicaRowBytes is not >= 0 ||
                    _compiled.MaximumRowBytes is not >= 0 || _baseline(null, Basis)?.CanReconcileExistingStream() != true)
                { RejectClient(); return; }
                Span<byte> proof = stackalloc byte[ProofBytes];
                BinaryPrimitives.WriteInt64LittleEndian(proof, CompiledSchema);
                _local.SharedSchemaProof.Span.CopyTo(proof[sizeof(long)..]);
                if (!Send(peer, Ready, proof)) RejectClient();
                else _prepared = true;
                break;
            case Command.Commit when !_committed && _local!.Committed.IsCompleted:
                if (!Succeeded(_local.Committed) || !_network.TryAdvanceGameplayRevision(Basis, Target) || _baseline(null, Target) is not { } receiver)
                { Stop(); return; }
                _receiveBaseline = new(receiver, peer, Id, Target);
                if (!Send(peer, Committed)) { Stop(); return; }
                _committed = true;
                break;
            case Command.Commit when !_baselineApplied && _receiveBaseline?.Complete == true:
                if (!_receiveBaseline.TryApplyAndAcknowledge(_network, _clearClientCaches)) { Stop(); return; }
                _baselineApplied = true;
                break;
            case Command.Resume when _resumeReceived && !_reportedResume && _local!.Completion.IsCompleted:
                if (!Succeeded(_local.Completion) || !Send(peer, Resumed)) { Stop(); return; }
                _reportedResume = true;
                break;
        }
    }

    private bool _startedPrepare, _abortInferred;

    // Borrowed span only. No Lua, artifact access, task wait or baseline application in packet callbacks.
    private void OnControl(IPeer peer, byte kind, long id, long revision, ReadOnlySpan<byte> payload)
    {
        if (_failed || _disposed || kind < InputCutoff || kind > Settled) return;
        if (!_server && _operation.Id == 0)
        {
            if (!payload.IsEmpty || _halt) return;
            if (kind == FailStop && (ulong)id == _lastSettled.Id && (ulong)revision == _lastSettled.Basis &&
                (ulong)_network.GameplayRevision == _lastSettled.Target && ReferenceEquals(peer, _settledServer)) HaltClient();
            else if (kind == Offer && id > _lastId && revision > 0 && revision < long.MaxValue &&
                _network.GameplayRevision == revision && ReferenceEquals(peer, _network.FirstPeer)) AcceptOffer(peer, id, revision);
            return;
        }
        if (id != Id || (kind is BaselineChunk or BaselineApplied or Resume or Resumed or Settled or Committed ? revision != Target : revision != Basis)) return;
        int index = IndexOf(peer);
        if (index < 0) return;
        if (_server)
        {
            ulong bit = 1UL << index;
            ulong participant = (ulong)index + 1;
            switch (kind)
            {
                case Accepted when _command == Command.Drain && payload.IsEmpty: _accepted |= bit; break;
                case Reject when payload.IsEmpty: _interrupted = true; break;
                case InputCutoff when (_started & bit) != 0: _drainBarrier?.TryObserve(peer, kind, id, revision, payload.Length); break;
                case Drained when _drainedLocal && payload.IsEmpty: _transition!.Receive(_operation, participant, Fact.Drained); break;
                case Ready when _command == Command.Prepare:
                    if (payload.Length != ProofBytes || BinaryPrimitives.ReadInt64LittleEndian(payload) != CompiledSchema ||
                        (_hasRemoteProof && !payload.SequenceEqual(_remoteProof)) || (_prepared && !ProofMatches(payload)))
                    { _interrupted = true; break; }
                    if (!_hasRemoteProof) { payload.CopyTo(_remoteProof); _hasRemoteProof = true; }
                    _transition!.Receive(_operation, participant, Fact.Ready);
                    break;
                case Committed when payload.IsEmpty: _transition!.Receive(_operation, participant, Fact.Committed); break;
                case BaselineApplied when _applied?.TryObserve(peer, kind, id, revision, payload.Length) == true:
                    _transition!.Receive(_operation, participant, Fact.BaselineApplied);
                    break;
                case Aborted when payload.IsEmpty: _aborted |= bit; break;
                case Resumed when _command == Command.Resume && payload.IsEmpty: _resumed |= bit; break;
            }
            return;
        }
        switch (kind)
        {
            case StartDrain when _command == Command.Drain && _started == 0 && !_abortReceived && payload.IsEmpty:
                _started = 1;
                _drainBarrier = Barrier(EffectCutoff, Basis);
                if (!TrySendInputCutoff(_network, peer, Id, Basis)) RejectClient();
                break;
            case EffectCutoff: _drainBarrier?.TryObserve(peer, kind, id, revision, payload.Length); break;
            case Prepare when _command == Command.Drain && _drainedLocal && payload.IsEmpty:
                _command = Command.Prepare; break;
            case Commit when _command == Command.Prepare && payload.IsEmpty:
                _decided = true;
                _command = Command.Commit;
                if (!_prepared || _abortReceived || !_local!.TryCommit()) HaltClient();
                break;
            case BaselineChunk when _command == Command.Commit:
                if (_receiveBaseline?.TryReceiveChunk(peer, kind, id, revision, payload) != true) HaltClient();
                break;
            case Abort when !_decided && !_abortReceived && payload.IsEmpty:
                BeginClientAbort();
                // Offer alone never closes gates. Release its reservation before a later ordered Offer in this poll.
                if (_started == 0 && _local!.Completion.IsCompletedSuccessfully && !_halt) Reset();
                break;
            case Restore when _abortAcknowledged && payload.IsEmpty:
                if (_network.TrySetGameplayGates(true, true)) Reset();
                else HaltClient();
                break;
            case Resume when _baselineApplied && !_resumeReceived:
                // The next ordered packet may be dispatched in this very poll.
                if (_receiveBaseline!.TryAcceptResume(_network, peer, kind, id, revision, payload.Length) && _local!.TryResume())
                { _resumeReceived = true; _command = Command.Resume; }
                else HaltClient();
                break;
            case Settled when _reportedResume && payload.IsEmpty:
                _lastSettled = _operation;
                _settledServer = peer;
                Reset();
                break;
            case FailStop when payload.IsEmpty: HaltClient(); break;
        }
    }

    private void AcceptOffer(IPeer peer, long id, long basis)
    {
        _lastId = id;
        if (!_network.TryReserveRevisionCohort(_members, out _count))
        { _network.TrySendRevisionControl(peer, Reject, id, basis, []); return; }
        if (_count != 1 || !ReferenceEquals(_members[0], peer) || _host.TryReserve(id, basis, basis + 1, out _local) != Status.Success)
        { _network.TrySendRevisionControl(peer, Reject, id, basis, []); Reset(); return; }
        _operation = new((ulong)id, (ulong)basis, (ulong)(basis + 1));
        _command = Command.Drain;
        _phaseStarted = Stopwatch.GetTimestamp();
        if (!Send(peer, Accepted)) RejectClient();
    }

    private void BeginClientAbort()
    {
        _abortReceived = true;
        _phaseStarted = Stopwatch.GetTimestamp();
        if (!_local!.TryAbort()) HaltClient();
    }

    private void SettleDisconnectedAbort(IPeer peer)
    {
        if (!_network.TrySetGameplayGates(true, true)) { Stop(); return; }
        if (_abortInferred) { Reset(); return; }
        // The old world and capture may reopen, but this disconnected session cannot admit a replacement peer.
        if (ReferenceEquals(_settledServer, peer)) _settledServer = null;
        Reset(releaseCohort: false);
    }

    private void RejectClient()
    {
        _interrupted = true;
        _phaseStarted = Stopwatch.GetTimestamp();
        Send(_members[0], Reject);
    }

    private bool ProofMatches(ReadOnlySpan<byte> proof) => _local!.SharedSchemaProof.Span.SequenceEqual(proof[sizeof(long)..]);
    private RevisionDrainBarrier Barrier(byte kind, long revision) => new(_members.AsSpan(0, _count).ToArray(), Id, revision, kind);
    private long Id => (long)_operation.Id;
    private long Basis => (long)_operation.Basis;
    private long Target => (long)_operation.Target;
    private ulong All => (1UL << _count) - 1;
    private bool Expired() => Stopwatch.GetElapsedTime(_phaseStarted) >= _timeout;
    private bool AllConnected() => ConnectedMask() == All;
    private ulong ConnectedMask()
    {
        ulong mask = 0;
        for (int i = 0; i < _count; i++)
            if (_members[i].ConnectionState == ConnectionState.Connected) mask |= 1UL << i;
        return mask;
    }
    private int IndexOf(IPeer peer)
    {
        for (int i = 0; i < _count; i++) if (ReferenceEquals(peer, _members[i])) return i;
        return -1;
    }
    private bool Send(IPeer peer, byte kind, ReadOnlySpan<byte> data = default) =>
        _network.TrySendRevisionControl(peer, kind, Id, kind is Committed or Resumed or Settled ? Target : Basis, data);
    private bool SendAll(byte kind, bool surviving = false, ulong mask = ulong.MaxValue)
    {
        bool delivered = true;
        for (int i = 0; i < _count; i++)
        {
            var peer = _members[i];
            if ((mask & (1UL << i)) == 0 || surviving && peer.ConnectionState != ConnectionState.Connected) continue;
            if (!Send(peer, kind) && !(surviving && peer.ConnectionState != ConnectionState.Connected)) delivered = false;
        }
        _interrupted |= !delivered;
        return delivered;
    }
    private static bool Succeeded(Task<SimulationCodeReloadResult> task) => task.IsCompletedSuccessfully && task.Result.Status == Status.Success;
    private static bool FailedCleanup(Task<SimulationCodeReloadResult> task) => task.IsCompleted &&
        (!task.IsCompletedSuccessfully || task.Result.Status == Status.Faulted);

    private void HaltClient()
    {
        _halt = true;
        _network.TrySetGameplayGates(false, false);
    }

    private void Stop()
    {
        _failed = true;
        try { if (_server) SendAll(FailStop, surviving: true); }
        finally
        {
            try { _network.TrySetGameplayGates(false, false); _network.Stop(); }
            finally { _host.FailStop(); Finish(Status.Faulted); }
        }
    }

    private void Finish(Status status)
    {
        var request = _request;
        Reset();
        request?.TrySetResult(status);
    }

    private void Reset(bool releaseCohort = true)
    {
        _local?.Release();
        if (releaseCohort) _network.ReleaseRevisionCohort();
        Array.Clear(_members, 0, _count);
        _count = 0; _operation = default; _command = Command.None; _cancel = default; _request = null; _local = null;
        _accepted = _started = _aborted = _resumed = 0;
        _drainBarrier = _applied = null; _receiveBaseline = null;
        _drainedLocal = _prepared = _committed = _decided = _baselineApplied = _resumeReceived = _reportedResume = false;
        _startedPrepare = _abortInferred = _abortReceived = _abortAcknowledged = _hasRemoteProof = _interrupted = _halt = false;
    }

    private void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Schema coordination must run on its transport owner.");
    }

    public void Dispose()
    {
        RequireOwner();
        if (_disposed) return;
        _disposed = true;
        _network.RevisionControlReceived -= OnControl;
        if (_operation.Id != 0) Stop(); // host teardown: do not leave a leased participant or a partially resumed session
    }
}
#endif
