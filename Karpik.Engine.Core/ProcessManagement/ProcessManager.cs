using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core;

internal class ProcessManager : IDisposable
{
    private static readonly AsyncLocal<ProcessManager?> s_lifecycleCallbackOwner = new();
    private Process? _workerProcess;
    private IpcServer? _ipcServer;
    private readonly string _workerExePath;
    private readonly string _bundlePath;
    private readonly string _engineRoot;
    private readonly string _pipeName;
    private readonly Side _side;
    private readonly HotReloadOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILoggerFactory? _ownedLoggerFactory;
    private readonly ILogger<ProcessManager> _logger;
    private readonly ModuleStagingCleanup _moduleStagingCleanup;
    private bool _hasStartedWorker;
    private int _reloadInProgress;
    private int _stopRequestCount;
    private int _disposeRequested;
    private WorkerExitNotification? _workerExitNotification;
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _transitionIntentGate = new();
    
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitorTask;
    private WorkerReadiness? _workerReadiness;
    private Action<IpcMessage>? _workerMessageHandler;
    private WorkerGeneration? _workerGeneration;
    private Action? _beforeLifecycleCallbackCommit;
    
    public event Action<int>? OnWorkerExited;
    public event Action? OnWorkerReady;
    public event Action<HotReloadState?>? OnHotReloadRequested;
    public event Action<string>? OnWorkerOutput;
    
    public bool IsWorkerRunning
    {
        get
        {
            if (_workerProcess == null)
                return false;
            
            try
            {
                return !_workerProcess.HasExited;
            }
            catch (InvalidOperationException)
            {
                // Ownership cleanup still needs the exact Process instance.
                return false;
            }
        }
    }
    
    public bool IsWorkerReady => Volatile.Read(ref _workerReadiness)?.IsReady ?? false;

    public bool IsReloadInProgress => Volatile.Read(ref _reloadInProgress) != 0;

    private bool IsDisposeRequested => Volatile.Read(ref _disposeRequested) != 0;

    private bool ShouldStopTransition =>
        IsDisposeRequested || Volatile.Read(ref _stopRequestCount) != 0;
    
    public int WorkerProcessId => Volatile.Read(ref _workerGeneration)?.ProcessId ?? -1;
    
    public ProcessManager(RuntimeLaunchOptions launchOptions, HotReloadOptions options, string? pipeName = null)
        : this(launchOptions, options, pipeName, HostLogging.CreateDefaultFactory(), ownsFactory: true)
    {
    }

    public ProcessManager(RuntimeLaunchOptions launchOptions, HotReloadOptions options, string? pipeName, ILoggerFactory loggerFactory)
        : this(launchOptions, options, pipeName, loggerFactory, ownsFactory: false)
    {
    }

    private ProcessManager(RuntimeLaunchOptions launchOptions, HotReloadOptions options, string? pipeName, ILoggerFactory loggerFactory, bool ownsFactory)
    {
        ArgumentNullException.ThrowIfNull(launchOptions);
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _ownedLoggerFactory = ownsFactory ? loggerFactory : null;
        _logger = loggerFactory.CreateLogger<ProcessManager>();
        _moduleStagingCleanup = new ModuleStagingCleanup(loggerFactory);
        _options = options;
        _workerExePath = launchOptions.RunnerExecutablePath;
        _bundlePath = launchOptions.BundlePath;
        _engineRoot = launchOptions.EngineRoot;
        _pipeName = pipeName ?? $"KarpikEngine_{Guid.NewGuid():N}";
        _side = launchOptions.Side;
    }

    [Obsolete("Legacy monorepository compatibility only. External runtimes must provide RuntimeLaunchOptions.")]
    public ProcessManager(Side side, HotReloadOptions options, string? pipeName = null)
        : this(side, options, pipeName, HostLogging.CreateDefaultFactory(), ownsFactory: true)
    {
    }

    public ProcessManager(Side side, HotReloadOptions options, string? pipeName, ILoggerFactory loggerFactory)
        : this(side, options, pipeName, loggerFactory, ownsFactory: false)
    {
    }

    private ProcessManager(Side side, HotReloadOptions options, string? pipeName, ILoggerFactory loggerFactory, bool ownsFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _ownedLoggerFactory = ownsFactory ? loggerFactory : null;
        _logger = loggerFactory.CreateLogger<ProcessManager>();
        _moduleStagingCleanup = new ModuleStagingCleanup(loggerFactory);
        _options = options;
        _workerExePath = options.WorkerExecutablePath ?? GetDefaultWorkerPath(_logger);
        _bundlePath = AppContext.BaseDirectory;
        _engineRoot = AppContext.BaseDirectory;
        _pipeName = pipeName ?? $"KarpikEngine_{Guid.NewGuid():N}";
        _side = side;
    }
    
    public string GetPipeName() => _pipeName;

    internal void SetLifecycleCallbackCommitHook(Action? callback)
    {
        Volatile.Write(ref _beforeLifecycleCallbackCommit, callback);
    }
    
    public async Task StartWorkerAsync(HotReloadState? initialState = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposeRequested, this);
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposeRequested, this);
            await StartWorkerCoreAsync(initialState, cancellationToken);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private async Task StartWorkerCoreAsync(
        HotReloadState? initialState,
        CancellationToken cancellationToken)
    {
        if (ShouldStopTransition)
        {
            return;
        }

        if (IsWorkerRunning)
        {
            _logger.LogInformation("Worker is already running");
            return;
        }
        
        if (!File.Exists(_workerExePath))
        {
            throw new FileNotFoundException(
                $"Worker executable was not found. Build the launcher project before starting hot reload. Expected path: {_workerExePath}",
                _workerExePath);
        }

        WorkerGeneration? abandonedGeneration = Volatile.Read(ref _workerGeneration);
        if (abandonedGeneration is not null)
        {
            await ReleaseWorkerGeneration(
                abandonedGeneration,
                disposeProcess: true,
                cleanupShadowCopies: true);
        }

        string? stateFile = null;
        if (initialState != null)
        {
            stateFile = WriteStateFile(initialState);
        }
        var shouldWaitForDebugger = _hasStartedWorker
            ? _options.WaitForDebuggerOnReloadWorkerStart
            : _options.WaitForDebuggerOnInitialWorkerStart;

        if (shouldWaitForDebugger)
        {
            // Added by CreateStartInfo below.
        }

        var startInfo = CreateStartInfo(
            new RuntimeLaunchOptions(_side, _workerExePath, _bundlePath, _engineRoot),
            _pipeName,
            stateFile,
            shouldWaitForDebugger,
            _options.CaptureWorkerOutput,
            _options.CaptureEditorLogs);

        if (ShouldStopTransition)
        {
            TryDeleteStateFile(stateFile);
            return;
        }

        using var connectionCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        connectionCts.CancelAfter(_options.WorkerConnectionTimeout);
        var readiness = new WorkerReadiness();
        var ipcServer = new IpcServer(_pipeName, _loggerFactory);
        var generation = new WorkerGeneration(readiness, ipcServer);
        Volatile.Write(ref _workerGeneration, generation);
        Volatile.Write(ref _workerReadiness, readiness);
        _ipcServer = ipcServer;
        Action<IpcMessage> messageHandler =
            message => HandleWorkerMessage(generation, message);
        generation.MessageHandler = messageHandler;
        _workerMessageHandler = messageHandler;
        ipcServer.OnMessageReceived += messageHandler;

        Process? workerProcess = null;
        try
        {
            Task ipcTask = ipcServer.WaitForConnectionAsync(connectionCts.Token);

            _logger.LogInformation("Starting worker {WorkerPath}", _workerExePath);
            _logger.LogInformation("Worker arguments: {Arguments}", string.Join(" ", startInfo.ArgumentList));

            workerProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            generation.Process = workerProcess;
            _workerProcess = workerProcess;

            var exitNotification = new WorkerExitNotification();
            generation.ExitNotification = exitNotification;
            _workerExitNotification = exitNotification;
            if (_options.CaptureWorkerOutput)
            {
                DataReceivedEventHandler outputHandler =
                    (_, args) => HandleWorkerOutput(generation, args.Data, isError: false);
                DataReceivedEventHandler errorHandler =
                    (_, args) => HandleWorkerOutput(generation, args.Data, isError: true);
                generation.OutputHandler = outputHandler;
                generation.ErrorHandler = errorHandler;
                workerProcess.OutputDataReceived += outputHandler;
                workerProcess.ErrorDataReceived += errorHandler;
            }

            EventHandler exitedHandler = (_, _) => HandleWorkerExited(generation);
            generation.ExitedHandler = exitedHandler;
            workerProcess.Exited += exitedHandler;

            if (!TryCommitWorkerStart(workerProcess, out bool startAttempted))
            {
                if (!startAttempted)
                {
                    await ReleaseWorkerGeneration(
                        generation,
                        disposeProcess: true,
                        cleanupShadowCopies: false);
                    TryDeleteStateFile(stateFile);
                    return;
                }
                throw new InvalidOperationException($"Failed to start worker process: {_workerExePath}");
            }

            generation.ProcessId = workerProcess.Id;
            if (_options.CaptureWorkerOutput)
            {
                workerProcess.BeginOutputReadLine();
                workerProcess.BeginErrorReadLine();
            }

            _logger.LogInformation("Worker started with PID {ProcessId}", generation.ProcessId);
            await ipcTask;
        }
        catch
        {
            try
            {
                if (workerProcess is not null && IsProcessRunning(workerProcess))
                {
                    await KillAndConfirmExitAsync(workerProcess);
                }
            }
            finally
            {
                await ReleaseWorkerGeneration(
                    generation,
                    disposeProcess: true,
                    cleanupShadowCopies: true);
                TryDeleteStateFile(stateFile);
            }
            throw;
        }

        _hasStartedWorker = true;
        _monitorTask = MonitorLoop(generation, _cts.Token);
    }

    private void HandleWorkerMessage(WorkerGeneration generation, IpcMessage msg)
    {
        bool ready = false;
        bool reloadRequested = false;
        generation.TryRun(() =>
        {
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            if (msg.Type == IpcMessageType.WorkerReady)
            {
                ready = true;
            }
            else if (msg.Type == IpcMessageType.HotReloadRequest)
            {
                reloadRequested = true;
            }
        });

        if (ready)
        {
            _logger.LogInformation("Worker is ready");
            CleanupCompletedModuleVersions(msg.Payload);
            generation.TryRun(() =>
            {
                if (!IsCurrentGeneration(generation))
                {
                    return;
                }

                generation.Readiness.MarkReady();
                Action? readyCallback = OnWorkerReady;
                if (readyCallback is not null)
                {
                    QueueGenerationCallback(generation, readyCallback);
                }
            });
        }

        if (reloadRequested)
        {
            _logger.LogInformation("Worker requested hot reload");
            _ = HandleWorkerReloadRequestAsync(generation);
        }
    }

    private void HandleWorkerOutput(
        WorkerGeneration generation,
        string? line,
        bool isError)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        generation.TryRun(() =>
        {
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            string output = isError ? $"[stderr] {line}" : line;
            QueueGenerationCallback(
                generation,
                () => OnWorkerOutput?.Invoke(output));
        });
    }

    private void HandleWorkerExited(WorkerGeneration generation)
    {
        int? exitCode = null;
        int processId = -1;
        generation.TryRun(() =>
        {
            if (!IsCurrentGeneration(generation) || generation.Process is not { } process)
            {
                return;
            }

            exitCode = process.ExitCode;
            processId = generation.ProcessId >= 0
                ? generation.ProcessId
                : process.Id;
        });

        if (exitCode is not { } code)
        {
            return;
        }

        _logger.LogInformation("Worker process exited with code {ExitCode}", code);
        CleanupWorkerShadowCopies(processId);
        generation.TryRun(() =>
        {
            if (!IsCurrentGeneration(generation))
            {
                return;
            }

            generation.Readiness.Stop();
            Action<int>? exited = OnWorkerExited;
            WorkerExitNotification? exitNotification = generation.ExitNotification;
            if (exitNotification is not null
                && !exitNotification.TryHandleExit(code)
                && exited is not null)
            {
                QueueGenerationCallback(generation, () => exited(code));
            }
        });
    }

    private async Task HandleWorkerReloadRequestAsync(WorkerGeneration generation)
    {
        try
        {
            if (!IsCurrentGeneration(generation) || !generation.IsActive)
            {
                return;
            }
            await HotReloadAsync(generation, _cts.Token);
        }
        catch (OperationCanceledException) when (IsDisposeRequested)
        {
        }
        catch (ObjectDisposedException) when (IsDisposeRequested)
        {
        }
    }
    
    public async Task<bool> WaitForWorkerReadyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        WorkerReadiness? readiness = Volatile.Read(ref _workerReadiness);
        if (readiness is null) return false;
        if (readiness.IsReady)
        {
            return ReferenceEquals(
                Volatile.Read(ref _workerReadiness),
                readiness);
        }

        Task readyTask = readiness.Completion;
        Task timeoutTask = Task.Delay(timeout, cancellationToken);
        Task completedTask = await Task.WhenAny(readyTask, timeoutTask);
        if (completedTask == timeoutTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
        await readyTask;
        return ReferenceEquals(Volatile.Read(ref _workerReadiness), readiness)
               && readiness.IsReady;
    }

    public Task HotReloadAsync(CancellationToken cancellationToken = default)
    {
        return HotReloadAsync(expectedGeneration: null, cancellationToken);
    }

    private async Task HotReloadAsync(
        WorkerGeneration? expectedGeneration,
        CancellationToken cancellationToken)
    {
        bool reloadOwned = false;
        if (expectedGeneration is null)
        {
            if (!TryBeginReload())
            {
                _logger.LogInformation("Hot reload is already in progress");
                return;
            }
            reloadOwned = true;
        }

        bool gateEntered = false;
        HotReloadState? notificationState = null;
        WorkerExitNotification? exitNotification = null;
        bool exitNotificationResolved = false;
        try
        {
            await _transitionGate.WaitAsync(cancellationToken);
            gateEntered = true;
            if (ShouldStopTransition)
            {
                return;
            }
            if (expectedGeneration is not null
                && (!IsCurrentGeneration(expectedGeneration)
                    || !expectedGeneration.IsActive))
            {
                return;
            }
            if (!reloadOwned)
            {
                if (!TryBeginReload())
                {
                    _logger.LogInformation("Hot reload is already in progress");
                    return;
                }
                reloadOwned = true;
            }

            WorkerGeneration? workerGeneration = Volatile.Read(ref _workerGeneration);
            IpcServer? ipcServer = workerGeneration?.IpcServer;
            Process? workerProcess = workerGeneration?.Process;
            exitNotification = workerGeneration?.ExitNotification;
            if (workerGeneration is null
                || ipcServer is null
                || workerProcess is null
                || exitNotification is null
                || !IsProcessRunning(workerProcess))
            {
                _logger.LogWarning("Cannot hot reload: worker not running");
                return;
            }

            _logger.LogInformation("Starting hot reload");

            // The runner exits immediately after sending its state. Defer exit publication
            // before requesting that state so the process cannot outrun the planned-exit marker.
            exitNotification.BeginDeferral();

            var (receivedState, state) = await ipcServer.TryRequestStateAsync(
                _options.StateRequestTimeout,
                cancellationToken);

            if (!receivedState || state == null)
            {
                _logger.LogWarning("Hot reload aborted: failed to collect ECS state. Existing worker remains running");
                return;
            }

            exitNotification.CommitSuppression();
            exitNotificationResolved = true;
            notificationState = state;

            if (ShouldStopTransition)
            {
                return;
            }

            bool exited = await WaitForExitAsync(workerProcess, _options.GracefulShutdownTimeout);
            if (!exited)
            {
                try
                {
                    await ipcServer.SendShutdownRequestAsync(
                        _options.GracefulShutdownTimeout,
                        CancellationToken.None);
                }
                catch (IOException)
                {
                    if (!await WaitForExitAsync(workerProcess, _options.GracefulShutdownTimeout))
                    {
                        throw;
                    }
                    exited = true;
                }

                if (!exited)
                {
                    exited = await WaitForExitAsync(workerProcess, _options.GracefulShutdownTimeout);
                }
            }

            if (!exited)
            {
                _logger.LogWarning("Worker did not exit gracefully; killing");
                await KillAndConfirmExitAsync(workerProcess);
            }

            await ReleaseWorkerGeneration(
                workerGeneration,
                disposeProcess: true,
                cleanupShadowCopies: true);

            if (ShouldStopTransition)
            {
                return;
            }

            await StartWorkerCoreAsync(state, CancellationToken.None);

            if (!ShouldStopTransition && IsWorkerRunning)
            {
                _logger.LogInformation("Hot reload complete");
            }
        }
        finally
        {
            if (exitNotification is not null && !exitNotificationResolved)
            {
                PublishDeferredWorkerExit(exitNotification);
            }
            if (reloadOwned)
            {
                EndReload();
                reloadOwned = false;
            }
            if (gateEntered)
            {
                _transitionGate.Release();
            }
            if (notificationState is not null)
            {
                OnHotReloadRequested?.Invoke(notificationState);
            }
        }
    }

    public async Task StopWorkerAsync(CancellationToken cancellationToken = default)
    {
        BeginStopRequest();
        bool gateEntered = false;
        try
        {
            await _transitionGate.WaitAsync(cancellationToken);
            gateEntered = true;
            await StopWorkerCoreAsync(cancellationToken);
        }
        finally
        {
            EndStopRequest();
            if (gateEntered)
            {
                _transitionGate.Release();
            }
        }
    }

    private async Task StopWorkerCoreAsync(CancellationToken cancellationToken)
    {
        WorkerGeneration? generation = Volatile.Read(ref _workerGeneration);
        if (generation is null)
        {
            return;
        }

        Process? workerProcess = generation.Process;
        IpcServer? ipcServer = generation.IpcServer;
        if (workerProcess is not null && IsProcessRunning(workerProcess))
        {
            _logger.LogInformation("Stopping worker");

            bool exited = false;
            if (ipcServer != null && ipcServer.IsConnected)
            {
                try
                {
                    await ipcServer.SendShutdownRequestAsync(_options.GracefulShutdownTimeout, cancellationToken);
                }
                catch (IOException)
                {
                    // A broken pipe can mean the worker exited before acknowledging.
                }
                exited = await WaitForExitAsync(workerProcess, _options.GracefulShutdownTimeout);
            }

            if (!exited)
            {
                _logger.LogWarning("Worker did not exit gracefully; killing");
                await KillAndConfirmExitAsync(workerProcess);
            }
        }

        await ReleaseWorkerGeneration(
            generation,
            disposeProcess: true,
            cleanupShadowCopies: true);
    }

    public Task<EditorRuntimeSnapshot?> RequestEditorSnapshotAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (_ipcServer is null || !IsWorkerReady)
        {
            return Task.FromResult<EditorRuntimeSnapshot?>(null);
        }

        return _ipcServer.RequestEditorSnapshotAsync(timeout, cancellationToken);
    }
    
    public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Process? workerProcess = _workerProcess;
        return workerProcess is null
            ? Task.FromResult(true)
            : WaitForExitAsync(workerProcess, timeout, cancellationToken);
    }

    internal static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, EventArgs e)
        {
            tcs.TrySetResult(true);
        }

        process.Exited += Handler;
        try
        {
            if (process.HasExited)
            {
                return true;
            }

            Task timeoutTask = Task.Delay(timeout, cancellationToken);
            Task completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
            if (completedTask == tcs.Task)
            {
                return true;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return process.HasExited;
        }
        finally
        {
            process.Exited -= Handler;
        }
    }

    private bool TryBeginReload() => Interlocked.CompareExchange(ref _reloadInProgress, 1, 0) == 0;

    private void EndReload() => Volatile.Write(ref _reloadInProgress, 0);

    private void BeginStopRequest()
    {
        lock (_transitionIntentGate)
        {
            Volatile.Write(ref _stopRequestCount, _stopRequestCount + 1);
        }
    }

    private void EndStopRequest()
    {
        lock (_transitionIntentGate)
        {
            Volatile.Write(ref _stopRequestCount, _stopRequestCount - 1);
        }
    }

    private bool TryCommitWorkerStart(Process process, out bool startAttempted)
    {
        lock (_transitionIntentGate)
        {
            if (IsDisposeRequested || _stopRequestCount != 0)
            {
                startAttempted = false;
                return false;
            }

            startAttempted = true;
            return process.Start();
        }
    }

    private void PublishDeferredWorkerExit(WorkerExitNotification exitNotification)
    {
        if (!exitNotification.CancelDeferral(out int exitCode))
        {
            return;
        }

        WorkerGeneration? generation = Volatile.Read(ref _workerGeneration);
        Action<int>? exited = OnWorkerExited;
        if (generation is not null
            && ReferenceEquals(generation.ExitNotification, exitNotification)
            && exited is not null)
        {
            QueueGenerationCallback(generation, () => exited(exitCode));
        }
    }

    private bool IsCurrentGeneration(WorkerGeneration generation)
    {
        return ReferenceEquals(Volatile.Read(ref _workerGeneration), generation);
    }

    private Task ReleaseWorkerGeneration(
        WorkerGeneration generation,
        bool disposeProcess,
        bool cleanupShadowCopies)
    {
        Task callbacksDrained = generation.Deactivate();
        Interlocked.CompareExchange(ref _workerGeneration, null, generation);

        Process? process = generation.Process;
        IpcServer ipcServer = generation.IpcServer;
        Action<IpcMessage>? messageHandler = generation.MessageHandler;
        EventHandler? exitedHandler = generation.ExitedHandler;
        DataReceivedEventHandler? outputHandler = generation.OutputHandler;
        DataReceivedEventHandler? errorHandler = generation.ErrorHandler;

        if (messageHandler is not null)
        {
            TryLifecycleCleanup(
                () => ipcServer.OnMessageReceived -= messageHandler,
                "IPC message handler");
        }
        if (process is not null)
        {
            if (exitedHandler is not null)
            {
                TryLifecycleCleanup(
                    () => process.Exited -= exitedHandler,
                    "process exit handler");
            }
            if (outputHandler is not null)
            {
                TryLifecycleCleanup(
                    () => process.OutputDataReceived -= outputHandler,
                    "process output handler");
            }
            if (errorHandler is not null)
            {
                TryLifecycleCleanup(
                    () => process.ErrorDataReceived -= errorHandler,
                    "process error handler");
            }
        }

        TryLifecycleCleanup(ipcServer.Dispose, "IPC server");
        if (cleanupShadowCopies && generation.ProcessId >= 0)
        {
            TryLifecycleCleanup(
                () => CleanupWorkerShadowCopies(generation.ProcessId),
                "worker shadow copies");
        }
        if (disposeProcess)
        {
            TryLifecycleCleanup(
                () => process?.Dispose(),
                "worker process");
        }

        if (ReferenceEquals(_ipcServer, ipcServer))
        {
            _ipcServer = null;
        }
        if (ReferenceEquals(_workerProcess, process))
        {
            _workerProcess = null;
        }
        if (ReferenceEquals(_workerExitNotification, generation.ExitNotification))
        {
            _workerExitNotification = null;
        }
        if (ReferenceEquals(_workerMessageHandler, messageHandler))
        {
            _workerMessageHandler = null;
        }
        if (ReferenceEquals(Volatile.Read(ref _workerReadiness), generation.Readiness))
        {
            Interlocked.CompareExchange(
                ref _workerReadiness,
                null,
                generation.Readiness);
        }
        return Task.WhenAll(callbacksDrained, ipcServer.WaitForListenerAsync());
    }

    private void TryLifecycleCleanup(Action cleanup, string resource)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to release {Resource}", resource);
        }
    }

    private async Task KillAndConfirmExitAsync(Process process)
    {
        if (!IsProcessRunning(process))
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (!IsProcessRunning(process))
        {
            return;
        }
        if (!await WaitForExitAsync(process, _options.GracefulShutdownTimeout))
        {
            throw new TimeoutException(
                $"Worker process {process.Id} did not exit after it was forcefully terminated.");
        }
    }

    private static bool IsProcessRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
    
    private async Task MonitorLoop(
        WorkerGeneration generation,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested
                   && IsCurrentGeneration(generation)
                   && generation.IsActive
                   && generation.Process is { } process
                   && IsProcessRunning(process))
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }
    
    private static string GetDefaultWorkerPath(ILogger logger)
    {
        var exeName = OperatingSystem.IsWindows() 
            ? "Karpik.Engine.Core.Runner.exe" 
            : "Karpik.Engine.Core.Runner";
        
        // Search locations in order of priority
        var searchPaths = new[]
        {
            AppContext.BaseDirectory
        };
        
        foreach (var searchPath in searchPaths)
        {
            var fullPath = Path.GetFullPath(Path.Combine(searchPath, exeName));
            if (File.Exists(fullPath))
            {
                logger.LogInformation("Found worker at {WorkerPath}", fullPath);
                return fullPath;
            }
        }
        
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, exeName);
        logger.LogWarning("Worker not found in any search location. Expected at {WorkerPath}. Make sure Karpik.Engine.Core.Runner is built and copied to the output directory", fallbackPath);
        return fallbackPath;
    }

    private static void TryDeleteStateFile(string? stateFile)
    {
        if (stateFile is null)
        {
            return;
        }

        try
        {
            File.Delete(stateFile);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string WriteStateFile(HotReloadState state)
    {
        var directory = Path.Combine(_bundlePath, "reload", "state");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, state.Serialize());
        return path;
    }

    private void CleanupWorkerShadowCopies(int processId)
    {
        var shadowRoot = Path.Combine(_bundlePath, "reload", "shadow");
        if (!Directory.Exists(shadowRoot))
        {
            return;
        }

        int candidates = 0;
        foreach (string directory in Directory.EnumerateDirectories(
                     shadowRoot,
                     $"{processId}_*",
                     SearchOption.TopDirectoryOnly))
        {
            if (++candidates > RuntimeBundleLayout.MaxTreeEntries)
            {
                _logger.LogWarning("Worker shadow cleanup exceeded the candidate bound");
                return;
            }
            try
            {
                string name = Path.GetFileName(directory);
                string suffix = name[(name.IndexOf('_') + 1)..];
                if (suffix.Length != 32
                    || !suffix.All(Uri.IsHexDigit)
                    || !RuntimeBundleLayout.IsBoundedTreeWithoutReparsePoints(directory))
                {
                    _logger.LogWarning("Refusing unproven worker shadow directory {Directory}", directory);
                    continue;
                }
                Directory.Delete(directory, recursive: true);
                _logger.LogInformation("Removed worker shadow directory {Directory}", directory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove worker shadow directory {Directory}", directory);
            }
        }
    }

    internal void CleanupCompletedModuleVersions(byte[] payload)
    {
        if (payload.Length is 0 or > 32_768)
        {
            return;
        }

        string activeDirectory;
        string expectedDirectory;
        try
        {
            activeDirectory = new System.Text.UTF8Encoding(false, true).GetString(payload);
            if (!Path.IsPathFullyQualified(activeDirectory))
            {
                return;
            }
            activeDirectory = Path.GetFullPath(activeDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            expectedDirectory = RuntimeBundleLayout.ResolveModuleDirectory(_bundlePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          || exception is ArgumentException
                                          || exception is System.Text.DecoderFallbackException
                                          || exception is IOException
                                          || exception is UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Ignoring invalid worker module staging directory");
            return;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(activeDirectory, expectedDirectory, comparison))
        {
            _logger.LogWarning("Ignoring worker module directory that does not exactly match the resolved bundle directory: {ActiveDirectory}", activeDirectory);
            return;
        }

        _moduleStagingCleanup.Cleanup(_bundlePath, activeDirectory);
    }

    internal static ProcessStartInfo CreateStartInfo(
        RuntimeLaunchOptions launchOptions,
        string pipeName,
        string? stateFile,
        bool waitForDebugger,
        bool captureOutput,
        bool captureEditorLogs = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = launchOptions.RunnerExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = captureOutput,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            WorkingDirectory = launchOptions.BundlePath
        };
        if (captureEditorLogs)
        {
            startInfo.EnvironmentVariables["KARPIK_EDITOR_LOG_CAPTURE"] = "1";
        }
        Add("--pipe-name", pipeName);
        Add("--side", launchOptions.Side.ToString());
        Add("--bundle", launchOptions.BundlePath);
        Add("--engine-root", launchOptions.EngineRoot);
        if (!string.IsNullOrEmpty(stateFile))
        {
            Add("--state-file", stateFile);
        }
        if (waitForDebugger)
        {
            startInfo.ArgumentList.Add("--wait-for-debugger");
        }
        string nativeDir = Path.Combine(launchOptions.EngineRoot, "native");
        if (Directory.Exists(nativeDir))
        {
            string currentPath = startInfo.EnvironmentVariables["PATH"];
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            }
            string rid = RuntimeInformation.RuntimeIdentifier;
            string osArch = rid[(rid.LastIndexOf('-') + 1)..];
            string baseRid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-" + osArch
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux-" + osArch
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-" + osArch
                : null;
            if (baseRid is not null)
            {
                string platformNative = Path.Combine(nativeDir, baseRid);
                if (Directory.Exists(platformNative))
                {
                    currentPath = platformNative + Path.PathSeparator + currentPath;
                }
            }
            currentPath = nativeDir + Path.PathSeparator + currentPath;
            startInfo.EnvironmentVariables["PATH"] = currentPath;
        }
        return startInfo;

        void Add(string name, string value)
        {
            startInfo.ArgumentList.Add(name);
            startInfo.ArgumentList.Add(value);
        }
    }

    private void QueueGenerationCallback(
        WorkerGeneration generation,
        Action callback)
    {
        QueueLifecycleCallback(() =>
        {
            if (!generation.TryReserveCallback(
                    () => IsCurrentGeneration(generation)))
            {
                return;
            }
            bool reservationOutstanding = true;
            try
            {
                Volatile.Read(ref _beforeLifecycleCallbackCommit)?.Invoke();
                if (!generation.TryCommitCallback())
                {
                    reservationOutstanding = false;
                    return;
                }
                reservationOutstanding = false;
                ProcessManager? previousOwner = s_lifecycleCallbackOwner.Value;
                s_lifecycleCallbackOwner.Value = this;
                try
                {
                    callback();
                }
                finally
                {
                    s_lifecycleCallbackOwner.Value = previousOwner;
                    generation.CompleteCommittedCallback();
                }
            }
            finally
            {
                if (reservationOutstanding)
                {
                    generation.CancelReservedCallback();
                }
            }
        });
    }

    private void QueueLifecycleCallback(Action callback)
    {
        ThreadPool.QueueUserWorkItem(
            action =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Lifecycle callback failed");
                }
            },
            callback,
            preferLocal: false);
    }
    
    public void Dispose()
    {
        lock (_transitionIntentGate)
        {
            if (IsDisposeRequested)
            {
                return;
            }
            Volatile.Write(ref _disposeRequested, 1);
        }

        _cts.Cancel();
        if (ReferenceEquals(s_lifecycleCallbackOwner.Value, this))
        {
            QueueLifecycleCallback(DisposeCore);
            return;
        }
        DisposeCore();
    }

    private void DisposeCore()
    {
        _transitionGate.Wait();
        try
        {
            WorkerGeneration? generation = Volatile.Read(ref _workerGeneration);
            Process? workerProcess = generation?.Process;
            if (generation is not null)
            {
                ReleaseWorkerGeneration(
                        generation,
                        disposeProcess: false,
                        cleanupShadowCopies: false)
                    .GetAwaiter()
                    .GetResult();
            }

            bool canDisposeWorker = true;
            try
            {
                if (workerProcess is not null && IsProcessRunning(workerProcess))
                {
                    int processId = workerProcess.Id;
                    workerProcess.Kill(entireProcessTree: true);
                    int timeoutMilliseconds = (int)Math.Clamp(
                        _options.GracefulShutdownTimeout.TotalMilliseconds,
                        1,
                        int.MaxValue);
                    canDisposeWorker = workerProcess.WaitForExit(timeoutMilliseconds);
                    if (canDisposeWorker)
                    {
                        CleanupWorkerShadowCopies(processId);
                    }
                    else
                    {
                        _logger.LogWarning("Worker process {ProcessId} did not confirm exit during disposal", processId);
                    }
                }
            }
            catch
            {
                canDisposeWorker = workerProcess is null || !IsProcessRunning(workerProcess);
            }

            if (canDisposeWorker)
            {
                workerProcess?.Dispose();
            }
        }
        finally
        {
            _transitionGate.Release();
            _cts.Dispose();
            _ownedLoggerFactory?.Dispose();
        }
    }

    private sealed class WorkerExitNotification
    {
        private readonly object _gate = new();
        private ExitDisposition _disposition;
        private bool _deferredExitPending;
        private int _deferredExitCode;

        public void BeginDeferral()
        {
            lock (_gate)
            {
                _disposition = ExitDisposition.Deferred;
                _deferredExitPending = false;
            }
        }

        public bool TryHandleExit(int exitCode)
        {
            lock (_gate)
            {
                if (_disposition == ExitDisposition.Publish)
                {
                    return false;
                }

                if (_disposition == ExitDisposition.Deferred)
                {
                    _deferredExitCode = exitCode;
                    _deferredExitPending = true;
                }

                _disposition = ExitDisposition.Consumed;
                return true;
            }
        }

        public void CommitSuppression()
        {
            lock (_gate)
            {
                _deferredExitPending = false;
                _disposition = ExitDisposition.Suppress;
            }
        }

        public bool CancelDeferral(out int exitCode)
        {
            lock (_gate)
            {
                exitCode = _deferredExitCode;
                if (_deferredExitPending)
                {
                    _deferredExitPending = false;
                    _disposition = ExitDisposition.Consumed;
                    return true;
                }

                if (_disposition == ExitDisposition.Deferred)
                {
                    _disposition = ExitDisposition.Publish;
                }
                return false;
            }
        }

        private enum ExitDisposition : byte
        {
            Publish,
            Deferred,
            Suppress,
            Consumed
        }
    }

    private sealed class WorkerGeneration
    {
        private readonly object _gate = new();
        private bool _active = true;
        private int _reservedCallbacks;
        private int _committedCallbacks;
        private TaskCompletionSource<bool>? _drained;

        public WorkerGeneration(WorkerReadiness readiness, IpcServer ipcServer)
        {
            Readiness = readiness;
            IpcServer = ipcServer;
        }

        public WorkerReadiness Readiness { get; }
        public IpcServer IpcServer { get; }
        public Process? Process { get; set; }
        public int ProcessId { get; set; } = -1;
        public WorkerExitNotification? ExitNotification { get; set; }
        public Action<IpcMessage>? MessageHandler { get; set; }
        public EventHandler? ExitedHandler { get; set; }
        public DataReceivedEventHandler? OutputHandler { get; set; }
        public DataReceivedEventHandler? ErrorHandler { get; set; }

        public bool IsActive
        {
            get
            {
                lock (_gate)
                {
                    return _active;
                }
            }
        }

        public bool TryRun(Action action)
        {
            lock (_gate)
            {
                if (!_active)
                {
                    return false;
                }
                action();
                return true;
            }
        }

        public bool TryReserveCallback(Func<bool> isCurrent)
        {
            lock (_gate)
            {
                if (!_active || !isCurrent())
                {
                    return false;
                }
                _reservedCallbacks++;
                return true;
            }
        }

        public bool TryCommitCallback()
        {
            lock (_gate)
            {
                _reservedCallbacks--;
                if (!_active)
                {
                    return false;
                }
                _committedCallbacks++;
                return true;
            }
        }

        public void CancelReservedCallback()
        {
            lock (_gate)
            {
                _reservedCallbacks--;
            }
        }

        public void CompleteCommittedCallback()
        {
            TaskCompletionSource<bool>? drained = null;
            lock (_gate)
            {
                _committedCallbacks--;
                if (!_active && _committedCallbacks == 0)
                {
                    drained = _drained;
                }
            }
            drained?.TrySetResult(true);
        }

        public Task Deactivate()
        {
            lock (_gate)
            {
                if (!_active)
                {
                    return _drained?.Task ?? Task.CompletedTask;
                }

                _active = false;
                Readiness.Stop();
                if (_committedCallbacks == 0)
                {
                    return Task.CompletedTask;
                }
                _drained = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                return _drained.Task;
            }
        }
    }

    private sealed class WorkerReadiness
    {
        private const int Pending = 0;
        private const int Ready = 1;
        private const int Stopped = 2;
        private int _state;
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsReady => Volatile.Read(ref _state) == Ready;

        public Task Completion => _completion.Task;

        public void MarkReady()
        {
            if (Interlocked.CompareExchange(ref _state, Ready, Pending) == Pending)
            {
                _completion.TrySetResult(true);
            }
        }

        public void Stop()
        {
            Interlocked.Exchange(ref _state, Stopped);
            _completion.TrySetCanceled();
        }
    }
}
