using System.Diagnostics;
using Karpik.Engine.Shared.ECS.Scheduling;
using LiveCommand = Karpik.Engine.Core.ModLiveTransition.Command;

namespace Karpik.Engine.Core;

/// <summary>Maintenance measurements; allocation is process-wide and may include concurrent host work.</summary>
public readonly record struct SimulationCodeReloadMetrics(double PauseSeconds, double PreparationSeconds,
    double CommitSeconds, double RetirementSeconds, long ProcessAllocatedBytes);

public partial class EngineRunner
{
    private sealed class MetricsSnapshot(SimulationCodeReloadMetrics value) { internal readonly SimulationCodeReloadMetrics Value = value; }
    private MetricsSnapshot _codeReloadMetrics = new(default);
    public SimulationCodeReloadMetrics LastCodeReloadMetrics => Volatile.Read(ref _codeReloadMetrics).Value;
    private bool _localCodeReloadEnabled, _networkedSimulation;
    private MainThreadScheduler? _codeReloadMainScheduler;
    private ISimulationCodeReloadParticipant? _codeReloadParticipant;
    private ISimulationCodeReloadBoundary? _codeReloadBoundary;
    private ISystemUpdate[] _codeReloadStableSystems = [];
    private EcsUpdateSystemDescriptor[] _codeReloadStableDescriptors = [];
    private int _codeReloadBusy, _codeReloadFrameCalls, _codeReloadExecutionFault, _codeReloadShuttingDown;
    private long _codeReloadPauseTicks;
    private readonly object _codeReloadRequestGate = new(); // explicit requests/shutdown only, never frame execution
    private CancellationTokenSource? _codeReloadCancellation;
    private Task<SimulationCodeReloadResult>? _codeReloadTask;
    private int _codeReloadCaptureCount;
    private TaskCompletionSource? _codeReloadCaptureDrain;

    public bool LocalCodeReloadEnabled => _localCodeReloadEnabled;
    public bool IsCodeReloading => Volatile.Read(ref _codeReloadBusy) != 0;
    public double CodeReloadPausedSeconds => (double)Interlocked.Read(ref _codeReloadPauseTicks) / Stopwatch.Frequency;

    public void EnableLocalCodeReload()
    {
        if (_setupPending || _engineContainer is not null)
            throw new InvalidOperationException("Local code reload must be explicitly enabled before setup.");
        _localCodeReloadEnabled = true;
    }

    public void SetCodeReloadBoundary(ISimulationCodeReloadBoundary boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        lock (_codeReloadRequestGate)
        {
            if (IsCodeReloading || Volatile.Read(ref _codeReloadShuttingDown) != 0)
                throw new InvalidOperationException("Cannot change the host boundary during reload or shutdown.");
            _codeReloadBoundary = boundary;
        }
    }

    public Task<SimulationCodeReloadResult> RequestCodeReloadAsync(CancellationToken cancellationToken = default)
    {
        lock (_codeReloadRequestGate)
        {
            if (IsLiveReloadReserved || IsCodeReloading || _codeReloadCaptureCount != 0) return Task.FromResult(new SimulationCodeReloadResult(SimulationCodeReloadStatus.Busy));
            if (!_localCodeReloadEnabled || _hostModLiveReload || _networkedSimulation
#if DEBUG
                || _hostSchemaReload
#endif
                )
                return Task.FromResult(new SimulationCodeReloadResult(SimulationCodeReloadStatus.Unsupported));
            if (!CanStartCodeReload()) return Task.FromResult(new SimulationCodeReloadResult(SimulationCodeReloadStatus.InvalidState));
            if (_codeReloadParticipant is not { } participant) return Task.FromResult(new SimulationCodeReloadResult(SimulationCodeReloadStatus.Unsupported));
            if (cancellationToken.IsCancellationRequested) return Task.FromResult(new SimulationCodeReloadResult(SimulationCodeReloadStatus.Cancelled));
            return StartCodeReload(async ct => await participant.PrepareReloadAsync(ct), cancellationToken);
        }
    }

    private bool CanStartCodeReload() => _pipeline is not null && !_setupPending && _codeReloadMainScheduler is not null &&
        Volatile.Read(ref _codeReloadShuttingDown) == 0 && Volatile.Read(ref _codeReloadExecutionFault) == 0 && _application.IsRunning;

    // The caller holds the request gate until the admission and its completion task are visible.
    private Task<SimulationCodeReloadResult> StartCodeReload(Func<CancellationToken, ValueTask<ISimulationCodeReloadCandidate?>> prepare,
        CancellationToken cancellationToken, LiveModReload? live = null)
    {
        var completion = new TaskCompletionSource<SimulationCodeReloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = _codeReloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _codeReloadTask = completion.Task;
        Volatile.Write(ref _codeReloadBusy, 1);
        _ = _codeReloadMainScheduler!.ScheduleAsync(async () =>
        {
            SimulationCodeReloadResult result;
            try { result = await ExecuteCodeReloadAsync(prepare, live, cancellation.Token); }
            catch (Exception error) { _application.Stop(); result = new(SimulationCodeReloadStatus.Faulted, error); }
            lock (_codeReloadRequestGate)
            {
                if (live is not null)
                {
                    if (live.FailStopped) result = new(SimulationCodeReloadStatus.Faulted, result.Error);
                    if (result.Status == SimulationCodeReloadStatus.Faulted)
                    {
                        Volatile.Write(ref _codeReloadShuttingDown, 1);
                        _application.Stop();
                    }
                    if (result.Status == SimulationCodeReloadStatus.Success) _liveModRevision = live.Operation.Target;
                    live.ReadySource.TrySetResult(result);
                    live.CommittedSource.TrySetResult(result);
                    _liveModReload = null;
                }
                _codeReloadCancellation = null;
                cancellation.Dispose();
                _codeReloadTask = null;
                Volatile.Write(ref _codeReloadBusy, 0);
                completion.TrySetResult(result);
            }
        });
        return completion.Task;
    }

    private async Task<SimulationCodeReloadResult> ExecuteCodeReloadAsync(
        Func<CancellationToken, ValueTask<ISimulationCodeReloadCandidate?>> prepare, LiveModReload? live,
        CancellationToken cancellationToken)
    {
        var main = _codeReloadMainScheduler!;
        ISimulationCodeReloadCandidate? candidate = null;
        EcsUpdateScheduler? preparedScheduler = null;
        IDisposable? lease = null;
        bool preparing = false, committed = false, tickerPaused = false;
        long pauseStarted = 0, preparationStarted = 0, preparationTicks = 0, commitTicks = 0, retirementTicks = 0;
        long allocationStarted = GC.GetTotalAllocatedBytes(precise: true);
        SimulationCodeReloadResult result = new(SimulationCodeReloadStatus.Rejected);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_codeReloadBoundary is not null)
            {
                var entry = await main.InvokeAsync(() => _codeReloadBoundary.EnterAsync(cancellationToken));
                lease = await entry;
            }
            while (Volatile.Read(ref _codeReloadFrameCalls) != 0)
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _codeReloadExecutionFault) != 0)
                throw new InvalidOperationException("A frame faulted before the reload boundary.");
            await main.InvokeAsync(() => { _fixedRunTicker.PauseForMaintenance(); tickerPaused = true; pauseStarted = Stopwatch.GetTimestamp(); });
            preparing = true;
            preparationStarted = Stopwatch.GetTimestamp();
            var preparation = await main.InvokeAsync(() => prepare(cancellationToken));
            candidate = await preparation;
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate is null) result = new(SimulationCodeReloadStatus.Busy);
            else result = await ActivatePreparedAsync(candidate);
        }
        catch (Exception error)
        {
            if (preparationStarted != 0) preparationTicks = Stopwatch.GetTimestamp() - preparationStarted;
            bool fatal = committed || !preparing && error is not OperationCanceledException || IsCodeReloadIntegrityFailure(error);
            result = new(fatal ? SimulationCodeReloadStatus.Faulted :
                error is OperationCanceledException ? SimulationCodeReloadStatus.Cancelled : SimulationCodeReloadStatus.Rejected, error);
        }
        finally
        {
            long cleanupStarted = Stopwatch.GetTimestamp();
            try
            {
                await main.InvokeAsync(() =>
                {
                    List<Exception>? errors = null;
                    try { preparedScheduler?.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
                    try { candidate?.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
                    try { if (tickerPaused) _fixedRunTicker.ResumeAfterMaintenance(); } catch (Exception error) { (errors ??= []).Add(error); }
                    try { lease?.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
                    if (errors is not null) throw new AggregateException("Code reload cleanup failed.", errors);
                });
            }
            catch (Exception cleanupError)
            {
                result = new(SimulationCodeReloadStatus.Faulted, result.Error is null ? cleanupError : new AggregateException(result.Error, cleanupError));
            }
            retirementTicks += Stopwatch.GetTimestamp() - cleanupStarted;
            long pauseTicks = pauseStarted == 0 ? 0 : Stopwatch.GetTimestamp() - pauseStarted;
            Interlocked.Add(ref _codeReloadPauseTicks, pauseTicks);
            Volatile.Write(ref _codeReloadMetrics, new MetricsSnapshot(new SimulationCodeReloadMetrics(
                (double)pauseTicks / Stopwatch.Frequency, (double)preparationTicks / Stopwatch.Frequency,
                (double)commitTicks / Stopwatch.Frequency, (double)retirementTicks / Stopwatch.Frequency,
                GC.GetTotalAllocatedBytes(precise: true) - allocationStarted)));
            if (result.Status == SimulationCodeReloadStatus.Faulted) _application.Stop();
        }
        return result;

        async Task<SimulationCodeReloadResult> ActivatePreparedAsync(ISimulationCodeReloadCandidate prepared)
        {
            await main.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var systems = new ISystemUpdate[_codeReloadStableSystems.Length + prepared.Systems.Length];
                var descriptors = new EcsUpdateSystemDescriptor[_codeReloadStableDescriptors.Length + prepared.Descriptors.Length];
                _codeReloadStableSystems.CopyTo(systems, 0);
                _codeReloadStableDescriptors.CopyTo(descriptors, 0);
                prepared.Systems.Span.CopyTo(systems.AsSpan(_codeReloadStableSystems.Length));
                prepared.Descriptors.Span.CopyTo(descriptors.AsSpan(_codeReloadStableDescriptors.Length));
                preparedScheduler = new EcsUpdateScheduler();
                preparedScheduler.InitializeInstances(systems, descriptors, UpdateSchedulerMode);
                prepared.PrepareActivation(cancellationToken);
                preparationTicks = Stopwatch.GetTimestamp() - preparationStarted;
                preparationStarted = 0;
            });
            if (live is not null)
            {
                lock (_codeReloadRequestGate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    live.SharedSchemaProof = prepared.SharedSchemaProof;
                    live.MaximumModReplicaRowBytes = prepared.MaximumModReplicaRowBytes;
#if DEBUG
                    if (live.IsSchemaReload)
                    {
                        live.SharedSchemaProof = ((IPreparedSchemaReload)prepared).SharedSchemaProof;
                        if (live.SharedSchemaProof.Length != 32) throw new InvalidDataException("No exact prepared Shared schema proof.");
                    }
#endif
                    live.ReadySource.SetResult(new(SimulationCodeReloadStatus.Success));
                }
                // After Ready, local shutdown cannot infer that the coordinator decided Abort.
                if (await live.Decision.Task.ConfigureAwait(false) != LiveCommand.Commit)
                    throw new OperationCanceledException("The host did not commit the live mod revision.");
                committed = true;
            }
            await main.InvokeAsync(() =>
            {
                if (live is null) cancellationToken.ThrowIfCancellationRequested();
                else if (live.FailStopped) throw new InvalidOperationException("The host failed before local publication began.");
                committed = true;
                var previous = _ecsUpdateScheduler;
                long commitStarted = Stopwatch.GetTimestamp();
                try
                {
                    prepared.Activate();
                    _ecsUpdateScheduler = preparedScheduler!;
                    preparedScheduler = null;
                }
                finally { commitTicks = Stopwatch.GetTimestamp() - commitStarted; }
                long retirementStarted = Stopwatch.GetTimestamp();
                try
                {
                    List<Exception>? errors = null;
                    try { previous.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
                    try { prepared.RetirePrevious(); } catch (Exception error) { (errors ??= []).Add(error); }
                    if (errors is not null) throw new AggregateException("Retired execution resources could not be released.", errors);
                    candidate = null;
                    prepared.Dispose();
                }
                finally { retirementTicks += Stopwatch.GetTimestamp() - retirementStarted; }
            });
            if (live is not null)
            {
                lock (_codeReloadRequestGate)
                {
                    if (live.FailStopped) throw new InvalidOperationException("The host failed the live mod revision.");
                    live.CommittedSource.SetResult(new(SimulationCodeReloadStatus.Success));
                }
                if (await live.Settlement.Task.ConfigureAwait(false) != LiveCommand.Resume)
                    throw new InvalidOperationException("The host failed the committed live mod revision.");
            }
            return new(SimulationCodeReloadStatus.Success);
        }
    }

    private static bool IsCodeReloadIntegrityFailure(Exception error) =>
        error is SimulationCodeReloadCleanupException or OutOfMemoryException ||
        error is AggregateException aggregate && aggregate.InnerExceptions.Any(IsCodeReloadIntegrityFailure);

    private void InitializeCodeReloadParticipants(ISimulationPreparation[] preparations, MainThreadScheduler scheduler)
    {
        _codeReloadMainScheduler = scheduler;
        _codeReloadParticipant = null;
        foreach (var preparation in preparations)
        {
            if (preparation is not ISimulationCodeReloadParticipant participant) continue;
            if (_codeReloadParticipant is not null)
                throw new InvalidOperationException("Only one code-reload backend can own a Simulation execution revision.");
            _codeReloadParticipant = participant;
        }
        Volatile.Write(ref _codeReloadShuttingDown, 0);
        Volatile.Write(ref _codeReloadExecutionFault, 0);
    }

    private void CaptureCodeReloadStableSystems(ISystemUpdate[] systems, EcsUpdateSystemDescriptor[] descriptors)
    {
        var replaced = _codeReloadParticipant?.ActiveSystems ?? ReadOnlyMemory<ISystemUpdate>.Empty;
        var stableSystems = new List<ISystemUpdate>();
        var stableDescriptors = new List<EcsUpdateSystemDescriptor>();
        for (int i = 0; i < systems.Length; i++)
        {
            bool revisionOwned = false;
            foreach (var system in replaced.Span) if (ReferenceEquals(system, systems[i])) { revisionOwned = true; break; }
            if (!revisionOwned) { stableSystems.Add(systems[i]); stableDescriptors.Add(descriptors[i]); }
        }
        _codeReloadStableSystems = stableSystems.ToArray();
        _codeReloadStableDescriptors = stableDescriptors.ToArray();
    }

    private bool CodeReloadFrameEntryClosed => IsCodeReloading || Volatile.Read(ref _codeReloadShuttingDown) != 0;

    private bool TryEnterCodeReloadFrame()
    {
        if (CodeReloadFrameEntryClosed) return false;
        Interlocked.Increment(ref _codeReloadFrameCalls);
        if (!CodeReloadFrameEntryClosed) return true;
        Interlocked.Decrement(ref _codeReloadFrameCalls);
        return false;
    }

    public void RunMainThreadBegin()
    {
        if (!TryEnterCodeReloadFrame()) return;
        try { RunMainThreadBeginCore(); }
        catch { Volatile.Write(ref _codeReloadExecutionFault, 1); throw; }
        finally { Interlocked.Decrement(ref _codeReloadFrameCalls); }
    }
    public void RunMainThreadFrameBegin()
    {
        if (!TryEnterCodeReloadFrame()) return;
        try { RunMainThreadFrameBeginCore(); }
        catch { Volatile.Write(ref _codeReloadExecutionFault, 1); throw; }
        finally { Interlocked.Decrement(ref _codeReloadFrameCalls); }
    }
    public void RunGameplayFrame(double dt)
    {
        if (!TryEnterCodeReloadFrame()) return;
        try { RunGameplayFrameCore(dt); }
        catch { Volatile.Write(ref _codeReloadExecutionFault, 1); throw; }
        finally { Interlocked.Decrement(ref _codeReloadFrameCalls); }
    }
    public void RunRender()
    {
        if (!TryEnterCodeReloadFrame()) return;
        try { RunRenderCore(); }
        catch { Volatile.Write(ref _codeReloadExecutionFault, 1); throw; }
        finally { Interlocked.Decrement(ref _codeReloadFrameCalls); }
    }

    /// <summary>Permanently closes frame entry and drains code reload before Simulation teardown.
    /// Ordinary reload cancellation uses the request cancellation token instead.</summary>
    public Task DrainCodeReloadForShutdownAsync()
    {
        lock (_codeReloadRequestGate)
        {
            Volatile.Write(ref _codeReloadShuttingDown, 1);
            if (_liveModReload is not { Ready.IsCompleted: true }) _codeReloadCancellation?.Cancel();
            if (_codeReloadTask is { } task) return task;
            if (_codeReloadCaptureCount == 0) return Task.CompletedTask;
            return (_codeReloadCaptureDrain ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private T CaptureOutsideCodeReload<T>(Func<T> capture)
    {
        lock (_codeReloadRequestGate)
        {
            if (IsLiveReloadReserved || CodeReloadFrameEntryClosed) throw new InvalidOperationException("State capture is unavailable during maintenance or shutdown.");
            _codeReloadCaptureCount++;
        }
        try { return capture(); }
        finally
        {
            lock (_codeReloadRequestGate)
            {
                if (--_codeReloadCaptureCount == 0)
                {
                    _codeReloadCaptureDrain?.TrySetResult();
                    _codeReloadCaptureDrain = null;
                }
            }
        }
    }

    private async Task DestroyAfterCodeReloadAsync()
    {
        StopContentReloadAdmission();
        Task pending = DrainCodeReloadForShutdownAsync();
        if (pending.IsCompleted && Volatile.Read(ref _codeReloadFrameCalls) == 0)
        {
            // Preserve existing shutdown threading when there was no in-flight reload.
            await DestroyAsyncCore(clearRegistrations: true);
            return;
        }
        await pending.ConfigureAwait(false);
        while (Volatile.Read(ref _codeReloadFrameCalls) != 0)
            await Task.Delay(1).ConfigureAwait(false);
        if (_codeReloadMainScheduler is { } main)
        {
            var cleanup = await main.InvokeAsync(() => DestroyAsyncCore(clearRegistrations: true).AsTask());
            await cleanup;
        }
        else await DestroyAsyncCore(clearRegistrations: true);
    }
}
