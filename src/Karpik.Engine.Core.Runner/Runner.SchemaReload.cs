#if DEBUG
using Command = Karpik.Engine.Core.ModLiveTransition.Command;
using Operation = Karpik.Engine.Core.ModLiveTransition.Operation;
using Status = Karpik.Engine.Core.SimulationCodeReloadStatus;

namespace Karpik.Engine.Core;

public partial class EngineRunner : ISimulationSchemaReloadHost
{
    private bool _hostSchemaReload, _schemaSettlementPending;

    /// <summary>Enable network-controlled Debug schema reload for one trusted host before setup.</summary>
    public ISimulationSchemaReloadHost EnableDebugSchemaReload(long initialRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialRevision);
        if (_setupPending || _engineContainer is not null || _hostModLiveReload || _hostSchemaReload)
            throw new InvalidOperationException("Only one live mod owner may be configured, before setup.");
        _liveModRevision = (ulong)initialRevision;
        _hostSchemaReload = true;
        return this;
    }

    Side ISimulationSchemaReloadHost.Side => _application.ApplicationSide;
    bool ISimulationSchemaReloadHost.IsRunning => _application.IsRunning && Volatile.Read(ref _codeReloadShuttingDown) == 0 &&
        Volatile.Read(ref _codeReloadExecutionFault) == 0;

    Status ISimulationSchemaReloadHost.TryReserve(long id, long basis, long target, out ISimulationSchemaReload? reload)
    {
        lock (_codeReloadRequestGate)
        {
            reload = null;
            if (!_hostSchemaReload || _codeReloadParticipant is not ISimulationSchemaReloadParticipant participant || !participant.SupportsSchemaReload)
                return Status.Unsupported;
            if (IsLiveReloadReserved || IsCodeReloading || _codeReloadCaptureCount != 0) return Status.Busy;
            if (id <= 0 || basis <= 0 || target <= basis || !CanStartCodeReload() ||
                (ulong)id <= _lastLiveModOperation || (ulong)basis != _liveModRevision) return Status.InvalidState;
            var live = new LiveModReload(new Operation((ulong)id, (ulong)basis, (ulong)target)) { IsSchemaReload = true };
            _lastLiveModOperation = (ulong)id;
            _liveModReload = live;
            _schemaSettlementPending = true;
            reload = new SchemaReload(this, participant, live);
            return Status.Success;
        }
    }

    void ISimulationSchemaReloadHost.FailStop()
    {
        lock (_codeReloadRequestGate)
        {
            if (!_hostSchemaReload) return;
            _schemaSettlementPending = false;
            Volatile.Write(ref _codeReloadShuttingDown, 1);
            _application.Stop();
            if (_liveModReload is { IsSchemaReload: true, Completion: not null } live)
                TryApplyLiveModCommand(live.Operation, Command.FailStop);
            else _liveModReload = null;
        }
    }

    private sealed class SchemaReload(EngineRunner runner, ISimulationSchemaReloadParticipant participant, LiveModReload live)
        : ISimulationSchemaReload
    {
        private readonly TaskCompletionSource<SimulationCodeReloadResult> _reservationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SimulationCodeReloadResult> Ready => live.Ready;
        public Task<SimulationCodeReloadResult> Committed => live.Committed;
        public Task<SimulationCodeReloadResult> Completion => live.Completion ?? _reservationCompletion.Task;
        public ReadOnlyMemory<byte> SharedSchemaProof => Ready.IsCompletedSuccessfully && Ready.Result.Status == Status.Success
            ? live.SharedSchemaProof : default;

        public int? MaximumModReplicaRowBytes => Ready.IsCompletedSuccessfully && Ready.Result.Status == Status.Success
            ? live.MaximumModReplicaRowBytes : null;

        public bool TryPrepare(Func<string, bool> admitLocalArtifact)
        {
            ArgumentNullException.ThrowIfNull(admitLocalArtifact);
            lock (runner._codeReloadRequestGate)
            {
                if (runner._liveModReload != live || live.Completion is not null || !runner.CanStartCodeReload()) return false;
                live.Completion = runner.StartCodeReload(async ct => await participant.PrepareSchemaReloadAsync(admitLocalArtifact, ct),
                    CancellationToken.None, live);
                return true;
            }
        }

        public bool TryAbort()
        {
            lock (runner._codeReloadRequestGate)
            {
                if (live.Completion is not null)
                    return live.Completion.IsCompletedSuccessfully && live.Completion.Result.Status is Status.Rejected or Status.Cancelled or Status.Busy
                        || runner.TryApplyLiveModCommand(live.Operation, Command.Abort);
                if (runner._liveModReload != live) return false;
                runner._liveModReload = null;
                var result = new SimulationCodeReloadResult(Status.Cancelled);
                live.ReadySource.TrySetResult(result);
                live.CommittedSource.TrySetResult(result);
                return _reservationCompletion.TrySetResult(result);
            }
        }

        public void Release()
        {
            lock (runner._codeReloadRequestGate)
                if (Completion.IsCompleted && live.Operation.Id == runner._lastLiveModOperation)
                    runner._schemaSettlementPending = false;
        }

        public bool TryCommit() => runner.TryApplyLiveModCommand(live.Operation, Command.Commit);
        public bool TryResume() => runner.TryApplyLiveModCommand(live.Operation, Command.Resume);
    }
}
#endif
