using Karpik.Content.Runtime;
using Command = Karpik.Engine.Core.ModLiveTransition.Command;
using Operation = Karpik.Engine.Core.ModLiveTransition.Operation;
using Status = Karpik.Engine.Core.SimulationCodeReloadStatus;

namespace Karpik.Engine.Core;

public partial class EngineRunner
{
    private bool _hostModLiveReload;
    private ulong _liveModRevision, _lastLiveModOperation;
    private LiveModReload? _liveModReload;
    private bool IsLiveReloadReserved => _liveModReload is not null
#if DEBUG
        || _schemaSettlementPending
#endif
        ;

    // For a future linked, network-capable host only. Nothing in standalone startup calls this.
    internal void EnableHostModLiveReload(ulong initialRevision = 0)
    {
        if (_setupPending || _engineContainer is not null || _hostModLiveReload
#if DEBUG
            || _hostSchemaReload
#endif
            )
            throw new InvalidOperationException("Live mod control must be enabled once before setup.");
        _liveModRevision = initialRevision;
        _hostModLiveReload = true;
    }

    internal Status BeginLiveModReload(Operation operation, IReadOnlyCollection<string> selectedIds, out LiveModReload? reload)
    {
        lock (_codeReloadRequestGate)
        {
            reload = null;
            if (!_hostModLiveReload || _codeReloadParticipant is not ISimulationLiveModParticipant participant)
                return Status.Unsupported;
            if (IsCodeReloading || _codeReloadCaptureCount != 0) return Status.Busy;
            if (!CanStartCodeReload() || operation.Id <= _lastLiveModOperation ||
                operation.Basis != _liveModRevision || operation.Target <= operation.Basis) return Status.InvalidState;
            if (selectedIds.Count > 256) return Status.Rejected; // Bound the local copy before entering the barrier.

            var ids = selectedIds.ToArray();
            var live = new LiveModReload(operation);
            _lastLiveModOperation = operation.Id;
            _liveModReload = reload = live;
            live.Completion = StartCodeReload(ct => participant.PrepareLiveModAsync(_contentCoordinator, ids, ReportContentDiagnostic, ct),
                CancellationToken.None, live);
            return Status.Success;
        }
    }

    internal bool TryApplyLiveModCommand(Operation operation, Command command)
    {
        lock (_codeReloadRequestGate)
        {
            if (_liveModReload is not { } live || operation != live.Operation) return false;
            switch (command)
            {
                case Command.Commit when live.Ready.IsCompletedSuccessfully && live.Ready.Result.Status == Status.Success:
                    return live.Decision.TrySetResult(command);
                case Command.Abort when live.Decision.TrySetResult(command):
                    if (!live.Ready.IsCompleted) _codeReloadCancellation!.Cancel();
                    return true;
                case Command.Resume when Volatile.Read(ref _codeReloadShuttingDown) == 0 &&
                    live.Committed.IsCompletedSuccessfully && live.Committed.Result.Status == Status.Success:
                    return live.Settlement.TrySetResult(command);
                case Command.FailStop when live.Settlement.TrySetResult(command):
                    // An uncertain decision can release resources only with permanent frame/capture closure.
                    Volatile.Write(ref _codeReloadShuttingDown, 1);
                    if (live.Decision.TrySetResult(command) && !live.Ready.IsCompleted) _codeReloadCancellation!.Cancel();
                    return true;
                default:
                    return false;
            }
        }
    }

    internal sealed class LiveModReload(Operation operation)
    {
        internal Operation Operation { get; } = operation;
        internal readonly TaskCompletionSource<SimulationCodeReloadResult> ReadySource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<SimulationCodeReloadResult> CommittedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<Command> Decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<Command> Settlement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<SimulationCodeReloadResult> Ready => ReadySource.Task;
        internal Task<SimulationCodeReloadResult> Committed => CommittedSource.Task;
        internal Task<SimulationCodeReloadResult> Completion { get; set; } = null!;
#if DEBUG
        internal bool IsSchemaReload { get; init; }
#endif
        internal ReadOnlyMemory<byte> SharedSchemaProof;
        internal int? MaximumModReplicaRowBytes;
        internal bool FailStopped => Settlement.Task.IsCompletedSuccessfully && Settlement.Task.Result == Command.FailStop;
    }
}
