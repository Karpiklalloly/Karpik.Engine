using Command = Karpik.Engine.Core.ModLiveTransition.Command;
using Kind = Karpik.Engine.Core.ModLiveIpcKind;
using Operation = Karpik.Engine.Core.ModLiveTransition.Operation;
using Status = Karpik.Engine.Core.SimulationCodeReloadStatus;

namespace Karpik.Engine.Core.Runner;

// Supplied only by a configured paired runtime. Control must never await the leased gameplay queue.
internal sealed record WorkerModLiveSupport(
    Func<CancellationToken, ValueTask<IReadOnlyCollection<string>>> Discover,
    Func<EngineRunner.LiveModReload, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> ValidatePrepared,
    Func<ModLiveTransition.Instruction, CancellationToken, ValueTask> NetworkPhase,
    Action<ModLiveIpcIdentity> BindIdentity, Task WireLoss);

internal sealed partial class WorkerHost
{
    private ModLiveWorkerControl? _liveControl;

    private void RunWithModLiveControl(MainThreadScheduler scheduler, Action<CancellationToken> loop, CancellationToken token)
    {
        if (_liveControl is not { } control)
        {
            loop(token);
            return;
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task pump = Task.Run(() => control.RunAsync(lifetime));
        try { loop(lifetime.Token); }
        finally
        {
            lifetime.Cancel();
            scheduler.RunUntilCompleted(pump);
        }
    }

    private void ConfigureModLiveCapture(IpcClient ipc, ModLiveWorkerControl control, MainThreadScheduler scheduler)
    {
        ipc.OnModLiveStateRequest = async token =>
        {
            if (!control.TryEnterCapture()) return null;
            try { return await scheduler.InvokeAsync(() => token.IsCancellationRequested ? null : GetHotReloadState()); }
            finally { control.ExitCapture(); }
        };
        ipc.OnModLiveEditorSnapshotRequest = async token =>
        {
            if (!control.TryEnterCapture()) return null;
            try
            {
                EditorRuntimeSnapshot? Capture() => token.IsCancellationRequested ? null : CaptureEditorSnapshotOnCurrentThread();
                return Volatile.Read(ref _clientSimulationWorker) is { } worker
                    ? await worker.InvokeAsync(Capture).ConfigureAwait(false)
                    : await scheduler.InvokeAsync(Capture);
            }
            finally { control.ExitCapture(); }
        };
    }

    private sealed class ModLiveWorkerControl(ModLiveIpcPeer peer, EngineRunner runner, WorkerModLiveSupport support)
    {
        private readonly object _captureGate = new();
        private bool _capture, _captureClosed;
        private Operation _active;
        private ulong _accepted = ModLiveIpcProtocol.InitialRevision, _last;
        private Kind _phase;
        private string[]? _selection;
        private EngineRunner.LiveModReload? _reload;
        private Task<ModLiveIpcMessage>? _pending;
        private CancellationTokenSource? _phaseCancellation;
        private bool _wireLossReported;

        private bool WatchWire => _active.Id != 0 && _phase != Kind.Abort && !_wireLossReported;

        internal bool TryEnterCapture()
        {
            lock (_captureGate)
            {
                if (_captureClosed || _capture || peer.Loss.IsCompleted || runner.IsCodeReloading) return false;
                return _capture = true;
            }
        }

        internal void ExitCapture()
        {
            lock (_captureGate) _capture = false;
        }

        internal async Task RunAsync(CancellationTokenSource lifetime)
        {
            var token = lifetime.Token;
            Task<bool>? read = null;
            try
            {
                while (!token.IsCancellationRequested && !peer.Loss.IsCompleted)
                {
                    read ??= peer.WaitToReadAsync(token).AsTask();
                    if (!read.IsCompleted && _pending?.IsCompleted != true)
                    {
                        var wire = WatchWire ? support.WireLoss : peer.Loss;
                        if (_pending is { } task) await Task.WhenAny(read, peer.Loss, wire, task).ConfigureAwait(false);
                        else await Task.WhenAny(read, peer.Loss, wire).ConfigureAwait(false);
                    }
                    if (token.IsCancellationRequested || peer.Loss.IsCompleted) break;
                    if (read.IsCompleted)
                    {
                        if (!await read.ConfigureAwait(false)) break;
                        read = null;
                        if (peer.TryRead(out var message) && !Accept(message, token)) break;
                    }
                    if (WatchWire && support.WireLoss.IsCompleted)
                    {
                        _wireLossReported = true;
                        await Send(new(Kind.Rejected, _active), token).ConfigureAwait(false);
                    }
                    if (_pending is { IsCompleted: true } finished)
                    {
                        var response = await finished.ConfigureAwait(false);
                        _pending = null;
                        _phaseCancellation?.Dispose();
                        _phaseCancellation = null;
                        // A candidate can be locally ready while the wire owner observes loss. Do not publish that Ready.
                        if (response.Kind == Kind.Ready && support.WireLoss.IsCompleted)
                        {
                            if (_wireLossReported) continue;
                            _wireLossReported = true;
                            response = new(Kind.Rejected, response.Operation);
                        }
                        await Send(response, token).ConfigureAwait(false);
                        if (response.Kind is Kind.Aborted or Kind.Resumed)
                        {
                            if (response.Kind == Kind.Resumed) _accepted = response.Operation.Target;
                            _active = default;
                            _wireLossReported = false;
                            _selection = null;
                            _reload = null;
                            lock (_captureGate) _captureClosed = false;
                        }
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException) { }
            finally
            {
                lock (_captureGate) _captureClosed = true;
                if (_reload is { } reload) runner.TryApplyLiveModCommand(reload.Operation, Command.FailStop);
                CancelPending();
                peer.Disconnect();
                lifetime.Cancel();
            }
        }

        private async Task Send(ModLiveIpcMessage response, CancellationToken token)
        {
            using var io = CancellationTokenSource.CreateLinkedTokenSource(token);
            io.CancelAfter(TimeSpan.FromSeconds(5));
            await peer.SendAsync(response, io.Token).ConfigureAwait(false);
        }

        private bool Accept(ModLiveIpcMessage request, CancellationToken token)
        {
            var operation = request.Operation;
            if (request.Kind == Kind.FailStop)
            {
                // A terminal command can also follow a Resume ACK from this side only.
                return operation != _active && operation.Id != _last;
            }
            if (_active.Id == 0)
            {
                if (_pending is not null || operation.Basis != _accepted || operation.Id <= _last) return true;
                if (request.Kind == Kind.Discover)
                {
                    _last = operation.Id;
                    Start(operation, Kind.Discovered, async ct =>
                        ModLiveIpcSelection.TryEncode(await support.Discover(ct).ConfigureAwait(false), out var ids)
                            ? (ReadOnlyMemory<byte>)ids : (ReadOnlyMemory<byte>?)null,
                        token);
                    return true;
                }
                if (request.Kind == Kind.Abort)
                {
                    _last = operation.Id;
                    Start(operation, Kind.Aborted, _ => ValueTask.FromResult<ReadOnlyMemory<byte>?>(ReadOnlyMemory<byte>.Empty), token);
                    return true;
                }
                if (request.Kind != Kind.Drain) return true;
                lock (_captureGate)
                {
                    if (_capture)
                    {
                        Start(operation, Kind.Rejected, _ => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null), token);
                        return true;
                    }
                    _captureClosed = true;
                }
                _last = operation.Id;
                _active = operation;
                _phase = Kind.Drain;
                StartNetwork(operation, Command.Drain, Kind.Drained, token);
                return true;
            }
            if (operation != _active) return true;
            if (request.Kind == Kind.Abort && _phase != Kind.Abort)
            {
                var cancelled = _pending;
                CancelPending();
                _phase = Kind.Abort;
                if (_reload is not null) runner.TryApplyLiveModCommand(operation, Command.Abort);
                Start(operation, Kind.Aborted, async ct =>
                {
                    if (cancelled is not null) await cancelled.ConfigureAwait(false);
                    if (_reload is { } abort && (await abort.Completion.ConfigureAwait(false)).Status == Status.Faulted) return null;
                    await support.NetworkPhase(new(operation, Command.Abort), ct).ConfigureAwait(false);
                    return ReadOnlyMemory<byte>.Empty;
                }, token);
                return true;
            }
            if (_pending is not null) return true;
            switch (request.Kind)
            {
                case Kind.Select when _phase == Kind.Drain:
                    if (!ModLiveIpcSelection.TryDecode(request.Payload.Span, out _selection)) return false;
                    _phase = Kind.Select;
                    break;
                case Kind.Prepare when _phase == Kind.Select:
                    _phase = Kind.Prepare;
                    if (runner.BeginLiveModReload(operation, _selection!, out _reload) != Status.Success)
                    {
                        Start(operation, Kind.Rejected, _ => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null), token);
                        break;
                    }
                    var preparing = _reload!;
                    Start(operation, Kind.Ready, async ct =>
                    {
                        if ((await preparing.Ready.WaitAsync(ct).ConfigureAwait(false)).Status != Status.Success) return null;
                        if (support.WireLoss.IsCompleted) return null;
                        var fingerprint = await support.ValidatePrepared(preparing, ct).ConfigureAwait(false);
                        if (fingerprint.Length != 32) return null;
                        return fingerprint;
                    }, token);
                    break;
                case Kind.Commit when _phase == Kind.Prepare && _reload is { } committing:
                    _phase = Kind.Commit;
                    if (!runner.TryApplyLiveModCommand(operation, Command.Commit)) return false;
                    Start(operation, Kind.Committed, async ct =>
                    {
                        if ((await committing.Committed.WaitAsync(ct).ConfigureAwait(false)).Status != Status.Success) return null;
                        await support.NetworkPhase(new(operation, Command.Commit), ct).ConfigureAwait(false);
                        return ReadOnlyMemory<byte>.Empty;
                    }, token);
                    break;
                case Kind.Baseline when _phase == Kind.Commit:
                    _phase = Kind.Baseline;
                    StartNetwork(operation, Command.Baseline, Kind.BaselineApplied, token);
                    break;
                case Kind.Resume when _phase == Kind.Baseline && _reload is { } resuming:
                    _phase = Kind.Resume;
                    Start(operation, Kind.Resumed, async ct =>
                    {
                        await support.NetworkPhase(new(operation, Command.Resume), ct).ConfigureAwait(false);
                        if (!runner.TryApplyLiveModCommand(operation, Command.Resume)) return null;
                        if ((await resuming.Completion.WaitAsync(ct).ConfigureAwait(false)).Status != Status.Success) return null;
                        return ReadOnlyMemory<byte>.Empty;
                    }, token);
                    break;
            }
            return true;
        }

        private void StartNetwork(Operation operation, Command command, Kind reply, CancellationToken token) =>
            Start(operation, reply, async ct =>
            {
                await support.NetworkPhase(new(operation, command), ct).ConfigureAwait(false);
                return ReadOnlyMemory<byte>.Empty;
            }, token);

        private void Start(Operation operation, Kind reply,
            Func<CancellationToken, ValueTask<ReadOnlyMemory<byte>?>> work, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var phase = _phaseCancellation = new CancellationTokenSource();
            _pending = Task.Run(async () =>
            {
                try
                {
                    var payload = await work(phase.Token).ConfigureAwait(false);
                    return payload is { } value ? new ModLiveIpcMessage(reply, operation, value) : new(Kind.Rejected, operation);
                }
                catch { return new(Kind.Rejected, operation); }
            });
        }

        private void CancelPending()
        {
            if (_phaseCancellation is not { } cancel) return;
            _phaseCancellation = null;
            // Cancellation registrations belong to the supplied work; don't run or join them on the pump.
            var cancelling = cancel.CancelAsync();
            _ = Task.WhenAll(cancelling, _pending ?? Task.CompletedTask).ContinueWith(t =>
            {
                _ = t.Exception;
                cancel.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
