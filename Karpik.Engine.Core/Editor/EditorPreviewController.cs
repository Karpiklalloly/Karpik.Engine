namespace Karpik.Engine.Core;

public enum EditorPreviewState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public sealed class EditorPreviewController : IDisposable
{
    private readonly ProcessManager _processManager;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _stateGate = new();
    private EditorPreviewState _state = EditorPreviewState.Stopped;
    private bool _disposed;
    private int _hotReloadInProgress;

    public Side Side { get; }
    public EditorPreviewState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }
    public int? ProcessId
    {
        get
        {
            int processId = _processManager.WorkerProcessId;
            return processId < 0 ? null : processId;
        }
    }

    public event Action<EditorPreviewState>? StateChanged;
    public event Action<string>? OutputReceived;

    public EditorPreviewController(RuntimeLaunchOptions launchOptions)
    {
        ArgumentNullException.ThrowIfNull(launchOptions);
        Side = launchOptions.Side;
        _processManager = new ProcessManager(
            launchOptions,
            new HotReloadOptions
            {
                Mode = HotReloadMode.RestartWorker,
                WorkerExecutablePath = launchOptions.RunnerExecutablePath,
                CaptureWorkerOutput = true
            });
        _processManager.OnWorkerOutput += HandleWorkerOutput;
        _processManager.OnWorkerExited += HandleWorkerExited;
    }

    [Obsolete("Legacy monorepository compatibility only. External previews must provide RuntimeLaunchOptions.")]
    public EditorPreviewController(Side side, string workerExecutablePath)
    {
        Side = side;
        _processManager = new ProcessManager(
            side,
            new HotReloadOptions
            {
                Mode = HotReloadMode.RestartWorker,
                WorkerExecutablePath = workerExecutablePath,
                CaptureWorkerOutput = true
            });
        _processManager.OnWorkerOutput += HandleWorkerOutput;
        _processManager.OnWorkerExited += HandleWorkerExited;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (State is EditorPreviewState.Starting or EditorPreviewState.Running)
            {
                return;
            }

            SetState(EditorPreviewState.Starting);
            try
            {
                await _processManager.StartWorkerAsync(cancellationToken: cancellationToken);
                bool ready = await _processManager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(30), cancellationToken);
                if (!ready)
                {
                    throw new TimeoutException("Preview worker did not report readiness within 30 seconds.");
                }

                if (!TrySetRunning())
                {
                    throw new InvalidOperationException(
                        "Preview worker exited before startup completed.");
                }
            }
            catch
            {
                SetState(EditorPreviewState.Faulted);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (State == EditorPreviewState.Stopped)
            {
                return;
            }

            SetState(EditorPreviewState.Stopping);
            await _processManager.StopWorkerAsync(cancellationToken);
            SetState(EditorPreviewState.Stopped);
        }
        catch
        {
            SetState(EditorPreviewState.Faulted);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task<EditorRuntimeSnapshot?> RequestSnapshotAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return State != EditorPreviewState.Running
            ? Task.FromResult<EditorRuntimeSnapshot?>(null)
            : _processManager.RequestEditorSnapshotAsync(timeout, cancellationToken);
    }

    public async Task HotReloadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (State != EditorPreviewState.Running)
            {
                throw new InvalidOperationException("Preview hot reload requires a running worker.");
            }

            int previousProcessId = _processManager.WorkerProcessId;
            Volatile.Write(ref _hotReloadInProgress, 1);
            try
            {
                await _processManager.HotReloadAsync(cancellationToken);
                bool ready = await _processManager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(30), cancellationToken);
                if (!ready
                    || !_processManager.IsWorkerRunning
                    || _processManager.WorkerProcessId == previousProcessId)
                {
                    throw new InvalidOperationException(
                        "Preview hot reload did not replace the worker with a ready process.");
                }
            }
            catch
            {
                if (!_processManager.IsWorkerRunning)
                {
                    SetState(EditorPreviewState.Faulted);
                }
                throw;
            }
            finally
            {
                Volatile.Write(ref _hotReloadInProgress, 0);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void SetState(EditorPreviewState state)
    {
        lock (_stateGate)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
        }

        StateChanged?.Invoke(state);
    }

    private bool TrySetRunning()
    {
        lock (_stateGate)
        {
            if (!CanPublishRunning(_state, _processManager.IsWorkerRunning))
            {
                return false;
            }

            _state = EditorPreviewState.Running;
        }

        StateChanged?.Invoke(EditorPreviewState.Running);
        return true;
    }

    internal static bool CanPublishRunning(EditorPreviewState state, bool workerRunning) =>
        state == EditorPreviewState.Starting && workerRunning;

    private void HandleWorkerOutput(string line) => OutputReceived?.Invoke(line);

    private void HandleWorkerExited(int exitCode)
    {
        if (Volatile.Read(ref _hotReloadInProgress) != 0)
        {
            return;
        }
        OutputReceived?.Invoke($"Preview завершён с кодом {exitCode}.");
        if (State is not EditorPreviewState.Stopping and not EditorPreviewState.Stopped)
        {
            SetState(exitCode == 0 ? EditorPreviewState.Stopped : EditorPreviewState.Faulted);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _processManager.OnWorkerOutput -= HandleWorkerOutput;
        _processManager.OnWorkerExited -= HandleWorkerExited;
        _processManager.Dispose();
    }
}
