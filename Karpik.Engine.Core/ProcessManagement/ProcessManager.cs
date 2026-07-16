using System.Diagnostics;

namespace Karpik.Engine.Core;

internal class ProcessManager : IDisposable
{
    private Process? _workerProcess;
    private IpcServer? _ipcServer;
    private readonly string _workerExePath;
    private readonly string _bundlePath;
    private readonly string _pipeName;
    private readonly Side _side;
    private readonly HotReloadOptions _options;
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
                // Process has been disposed or is no longer valid
                _workerProcess = null;
                return false;
            }
        }
    }
    
    public bool IsWorkerReady => Volatile.Read(ref _workerReadiness)?.IsReady ?? false;

    public bool IsReloadInProgress => Volatile.Read(ref _reloadInProgress) != 0;

    private bool IsDisposeRequested => Volatile.Read(ref _disposeRequested) != 0;

    private bool ShouldStopTransition =>
        IsDisposeRequested || Volatile.Read(ref _stopRequestCount) != 0;
    
    public int WorkerProcessId => _workerProcess?.Id ?? -1;
    
    public ProcessManager(RuntimeLaunchOptions launchOptions, HotReloadOptions options, string? pipeName = null)
    {
        ArgumentNullException.ThrowIfNull(launchOptions);
        _options = options;
        _workerExePath = launchOptions.RunnerExecutablePath;
        _bundlePath = launchOptions.BundlePath;
        _pipeName = pipeName ?? $"KarpikEngine_{Guid.NewGuid():N}";
        _side = launchOptions.Side;
    }

    [Obsolete("Legacy monorepository compatibility only. External runtimes must provide RuntimeLaunchOptions.")]
    public ProcessManager(Side side, HotReloadOptions options, string? pipeName = null)
    {
        _options = options;
        _workerExePath = options.WorkerExecutablePath ?? GetDefaultWorkerPath();
        _bundlePath = AppContext.BaseDirectory;
        _pipeName = pipeName ?? $"KarpikEngine_{Guid.NewGuid():N}";
        _side = side;
    }
    
    public string GetPipeName() => _pipeName;
    
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
            Console.WriteLine("[ProcessManager] Worker is already running");
            return;
        }
        
        if (!File.Exists(_workerExePath))
        {
            throw new FileNotFoundException(
                $"Worker executable was not found. Build the launcher project before starting hot reload. Expected path: {_workerExePath}",
                _workerExePath);
        }

        var readiness = new WorkerReadiness();
        Volatile.Write(ref _workerReadiness, readiness);

        IpcServer? previousIpcServer = _ipcServer;
        Action<IpcMessage>? previousMessageHandler = _workerMessageHandler;
        if (previousIpcServer is not null)
        {
            if (previousMessageHandler is not null)
            {
                previousIpcServer.OnMessageReceived -= previousMessageHandler;
            }
            previousIpcServer.Dispose();
        }
        
        var ipcServer = new IpcServer(_pipeName);
        _ipcServer = ipcServer;
        Action<IpcMessage> messageHandler =
            message => HandleWorkerMessage(ipcServer, readiness, message);
        _workerMessageHandler = messageHandler;
        ipcServer.OnMessageReceived += messageHandler;
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        connectionCts.CancelAfter(_options.WorkerConnectionTimeout);
        
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
            new RuntimeLaunchOptions(_side, _workerExePath, _bundlePath),
            _pipeName,
            stateFile,
            shouldWaitForDebugger,
            _options.CaptureWorkerOutput);

        if (ShouldStopTransition)
        {
            ipcServer.OnMessageReceived -= messageHandler;
            ipcServer.Dispose();
            if (ReferenceEquals(_ipcServer, ipcServer))
            {
                _ipcServer = null;
            }
            if (ReferenceEquals(_workerMessageHandler, messageHandler))
            {
                _workerMessageHandler = null;
            }
            if (ReferenceEquals(Volatile.Read(ref _workerReadiness), readiness))
            {
                Interlocked.CompareExchange(ref _workerReadiness, null, readiness);
            }
            TryDeleteStateFile(stateFile);
            return;
        }

        var ipcTask = ipcServer.WaitForConnectionAsync(connectionCts.Token);

        Console.WriteLine($"[ProcessManager] Starting worker: {_workerExePath}");
        Console.WriteLine($"[ProcessManager] Arguments: {string.Join(" ", startInfo.ArgumentList)}");

        _workerProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        
        var capturedProcess = _workerProcess;
        var exitNotification = new WorkerExitNotification();
        _workerExitNotification = exitNotification;
        if (_options.CaptureWorkerOutput)
        {
            capturedProcess.OutputDataReceived += (_, args) => PublishWorkerOutput(args.Data, isError: false);
            capturedProcess.ErrorDataReceived += (_, args) => PublishWorkerOutput(args.Data, isError: true);
        }

        capturedProcess.Exited += (sender, e) =>
        {
            var exitCode = capturedProcess.ExitCode;
            Console.WriteLine($"[ProcessManager] Worker process exited with code: {exitCode}");
            readiness.Stop();
            CleanupWorkerShadowCopies(capturedProcess.Id);
            Action<int>? exited = OnWorkerExited;
            if (!exitNotification.TryHandleExit(exitCode) && exited is not null)
            {
                QueueLifecycleCallback(() => exited(exitCode));
            }
        };
        
        if (!TryCommitWorkerStart(_workerProcess, out bool startAttempted))
        {
            if (!startAttempted)
            {
                ipcServer.OnMessageReceived -= messageHandler;
                ipcServer.Dispose();
                capturedProcess.Dispose();
                if (ReferenceEquals(_workerProcess, capturedProcess))
                {
                    _workerProcess = null;
                }
                if (ReferenceEquals(_workerExitNotification, exitNotification))
                {
                    _workerExitNotification = null;
                }
                if (ReferenceEquals(_ipcServer, ipcServer))
                {
                    _ipcServer = null;
                }
                if (ReferenceEquals(_workerMessageHandler, messageHandler))
                {
                    _workerMessageHandler = null;
                }
                if (ReferenceEquals(Volatile.Read(ref _workerReadiness), readiness))
                {
                    Interlocked.CompareExchange(ref _workerReadiness, null, readiness);
                }
                TryDeleteStateFile(stateFile);
                return;
            }
            throw new InvalidOperationException($"Failed to start worker process: {_workerExePath}");
        }

        if (_options.CaptureWorkerOutput)
        {
            _workerProcess.BeginOutputReadLine();
            _workerProcess.BeginErrorReadLine();
        }
        
        Console.WriteLine($"[ProcessManager] Worker started with PID: {_workerProcess.Id}");
        
        try
        {
            await ipcTask;
        }
        catch
        {
            Process? failedProcess = _workerProcess;
            if (failedProcess is not null && IsProcessRunning(failedProcess))
            {
                await KillAndConfirmExitAsync(failedProcess);
            }

            ipcServer.OnMessageReceived -= messageHandler;
            ipcServer.Dispose();
            if (ReferenceEquals(_ipcServer, ipcServer))
            {
                _ipcServer = null;
            }
            if (ReferenceEquals(_workerMessageHandler, messageHandler))
            {
                _workerMessageHandler = null;
            }
            failedProcess?.Dispose();
            if (ReferenceEquals(_workerProcess, failedProcess))
            {
                _workerProcess = null;
            }
            if (ReferenceEquals(_workerExitNotification, exitNotification))
            {
                _workerExitNotification = null;
            }
            if (ReferenceEquals(Volatile.Read(ref _workerReadiness), readiness))
            {
                Interlocked.CompareExchange(ref _workerReadiness, null, readiness);
            }
            TryDeleteStateFile(stateFile);
            throw;
        }

        _hasStartedWorker = true;
        _monitorTask = MonitorLoop(_cts.Token);
    }

    private void HandleWorkerMessage(
        IpcServer ipcServer,
        WorkerReadiness readiness,
        IpcMessage msg)
    {
        if (!ReferenceEquals(Volatile.Read(ref _ipcServer), ipcServer)
            || !ReferenceEquals(Volatile.Read(ref _workerReadiness), readiness))
        {
            return;
        }

        if (msg.Type == IpcMessageType.WorkerReady)
        {
            Console.WriteLine("[ProcessManager] Worker is ready");
            CleanupCompletedModuleVersions(msg.Payload);
            readiness.MarkReady();
            Action? ready = OnWorkerReady;
            if (ready is not null)
            {
                QueueLifecycleCallback(ready);
            }
        }
        else if (msg.Type == IpcMessageType.HotReloadRequest)
        {
            Console.WriteLine("[ProcessManager] Worker requested hot reload");
            _ = HandleWorkerReloadRequestAsync(ipcServer, readiness);
        }
    }

    private async Task HandleWorkerReloadRequestAsync(
        IpcServer ipcServer,
        WorkerReadiness readiness)
    {
        try
        {
            if (!ReferenceEquals(Volatile.Read(ref _ipcServer), ipcServer)
                || !ReferenceEquals(Volatile.Read(ref _workerReadiness), readiness))
            {
                return;
            }
            await HotReloadAsync(ipcServer, readiness, _cts.Token);
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
        if (readiness.IsReady) return true;

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
        return HotReloadAsync(
            expectedIpcServer: null,
            expectedReadiness: null,
            cancellationToken);
    }

    private async Task HotReloadAsync(
        IpcServer? expectedIpcServer,
        WorkerReadiness? expectedReadiness,
        CancellationToken cancellationToken)
    {
        if (!TryBeginReload())
        {
            Console.WriteLine("[ProcessManager] Hot reload is already in progress");
            return;
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
            if (expectedIpcServer is not null
                && (!ReferenceEquals(Volatile.Read(ref _ipcServer), expectedIpcServer)
                    || !ReferenceEquals(
                        Volatile.Read(ref _workerReadiness),
                        expectedReadiness)))
            {
                return;
            }

            IpcServer? ipcServer = _ipcServer;
            Process? workerProcess = _workerProcess;
            WorkerReadiness? workerReadiness = Volatile.Read(ref _workerReadiness);
            Action<IpcMessage>? workerMessageHandler = _workerMessageHandler;
            exitNotification = _workerExitNotification;
            if (ipcServer is null
                || workerProcess is null
                || workerReadiness is null
                || workerMessageHandler is null
                || exitNotification is null
                || !IsProcessRunning(workerProcess))
            {
                Console.WriteLine("[ProcessManager] Cannot hot reload: worker not running");
                return;
            }

            Console.WriteLine("[ProcessManager] Starting hot reload...");

            // The runner exits immediately after sending its state. Defer exit publication
            // before requesting that state so the process cannot outrun the planned-exit marker.
            exitNotification.BeginDeferral();

            var (receivedState, state) = await ipcServer.TryRequestStateAsync(
                _options.StateRequestTimeout,
                cancellationToken);

            if (!receivedState || state == null)
            {
                Console.WriteLine("[ProcessManager] Hot reload aborted: failed to collect ECS state. Existing worker remains running.");
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
                Console.WriteLine("[ProcessManager] Worker didn't exit gracefully, killing...");
                await KillAndConfirmExitAsync(workerProcess);
            }

            ipcServer.OnMessageReceived -= workerMessageHandler;
            ipcServer.Dispose();
            if (ReferenceEquals(_ipcServer, ipcServer))
            {
                _ipcServer = null;
            }
            if (ReferenceEquals(_workerMessageHandler, workerMessageHandler))
            {
                _workerMessageHandler = null;
            }
            workerProcess.Dispose();
            if (ReferenceEquals(_workerProcess, workerProcess))
            {
                _workerProcess = null;
            }
            if (ReferenceEquals(_workerExitNotification, exitNotification))
            {
                _workerExitNotification = null;
            }
            if (ReferenceEquals(Volatile.Read(ref _workerReadiness), workerReadiness))
            {
                Interlocked.CompareExchange(ref _workerReadiness, null, workerReadiness);
            }

            if (ShouldStopTransition)
            {
                return;
            }

            await StartWorkerCoreAsync(state, CancellationToken.None);

            if (!ShouldStopTransition && IsWorkerRunning)
            {
                Console.WriteLine("[ProcessManager] Hot reload complete!");
            }
        }
        finally
        {
            if (exitNotification is not null && !exitNotificationResolved)
            {
                PublishDeferredWorkerExit(exitNotification);
            }
            if (gateEntered)
            {
                _transitionGate.Release();
            }
            try
            {
                if (notificationState is not null)
                {
                    OnHotReloadRequested?.Invoke(notificationState);
                }
            }
            finally
            {
                EndReload();
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
        Process? workerProcess = _workerProcess;
        if (workerProcess is null)
        {
            return;
        }

        IpcServer? ipcServer = _ipcServer;
        WorkerExitNotification? exitNotification = _workerExitNotification;
        WorkerReadiness? readiness = Volatile.Read(ref _workerReadiness);
        Action<IpcMessage>? messageHandler = _workerMessageHandler;
        if (IsProcessRunning(workerProcess))
        {
            Console.WriteLine("[ProcessManager] Stopping worker...");

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
                Console.WriteLine("[ProcessManager] Worker didn't exit gracefully, killing...");
                await KillAndConfirmExitAsync(workerProcess);
            }
        }
        
        int workerProcessId = workerProcess.Id;
        if (ipcServer is not null)
        {
            if (messageHandler is not null)
            {
                ipcServer.OnMessageReceived -= messageHandler;
            }
            ipcServer.Dispose();
        }
        if (ReferenceEquals(_ipcServer, ipcServer))
        {
            _ipcServer = null;
        }
        if (ReferenceEquals(_workerMessageHandler, messageHandler))
        {
            _workerMessageHandler = null;
        }
        CleanupWorkerShadowCopies(workerProcessId);
        workerProcess.Dispose();
        if (ReferenceEquals(_workerProcess, workerProcess))
        {
            _workerProcess = null;
        }
        readiness?.Stop();
        if (ReferenceEquals(Volatile.Read(ref _workerReadiness), readiness))
        {
            Interlocked.CompareExchange(ref _workerReadiness, null, readiness);
        }
        if (ReferenceEquals(_workerExitNotification, exitNotification))
        {
            _workerExitNotification = null;
        }
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

        Action<int>? exited = OnWorkerExited;
        if (exited is not null)
        {
            QueueLifecycleCallback(() => exited(exitCode));
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
    
    private async Task MonitorLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsWorkerRunning)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }
    
    private static string GetDefaultWorkerPath()
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
                Console.WriteLine($"[ProcessManager] Found worker at: {fullPath}");
                return fullPath;
            }
        }
        
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, exeName);
        Console.WriteLine($"[ProcessManager] Worker not found in any search location. Expected at: {fallbackPath}");
        Console.WriteLine("[ProcessManager] Make sure Karpik.Engine.Core.Runner is built and copied to the output directory.");
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
                Console.WriteLine("[ProcessManager] Worker shadow cleanup exceeded the candidate bound.");
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
                    Console.WriteLine($"[ProcessManager] Refusing unproven worker shadow directory: {directory}");
                    continue;
                }
                Directory.Delete(directory, recursive: true);
                Console.WriteLine($"[ProcessManager] Removed worker shadow directory: {directory}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessManager] Failed to remove worker shadow directory '{directory}': {ex.Message}");
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
            Console.WriteLine($"[ProcessManager] Ignoring invalid worker module staging directory: {exception.Message}");
            return;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(activeDirectory, expectedDirectory, comparison))
        {
            Console.WriteLine($"[ProcessManager] Ignoring worker module directory that does not exactly match the resolved bundle directory: {activeDirectory}");
            return;
        }

        ModuleStagingCleanup.CleanupCompletedVersions(_bundlePath, activeDirectory);
    }

    internal static ProcessStartInfo CreateStartInfo(
        RuntimeLaunchOptions launchOptions,
        string pipeName,
        string? stateFile,
        bool waitForDebugger,
        bool captureOutput)
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
        Add("--pipe-name", pipeName);
        Add("--side", launchOptions.Side.ToString());
        Add("--bundle", launchOptions.BundlePath);
        if (!string.IsNullOrEmpty(stateFile))
        {
            Add("--state-file", stateFile);
        }
        if (waitForDebugger)
        {
            startInfo.ArgumentList.Add("--wait-for-debugger");
        }
        return startInfo;

        void Add(string name, string value)
        {
            startInfo.ArgumentList.Add(name);
            startInfo.ArgumentList.Add(value);
        }
    }

    private void PublishWorkerOutput(string? line, bool isError)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        OnWorkerOutput?.Invoke(isError ? $"[stderr] {line}" : line);
    }

    private static void QueueLifecycleCallback(Action callback)
    {
        ThreadPool.QueueUserWorkItem(
            static action =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"[ProcessManager] Lifecycle callback failed: {exception.Message}");
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
        _transitionGate.Wait();
        try
        {
            Process? workerProcess = _workerProcess;
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
                        Console.WriteLine($"[ProcessManager] Worker process {processId} did not confirm exit during disposal.");
                    }
                }
            }
            catch
            {
                canDisposeWorker = workerProcess is null || !IsProcessRunning(workerProcess);
            }

            IpcServer? ipcServer = _ipcServer;
            Action<IpcMessage>? messageHandler = _workerMessageHandler;
            _ipcServer = null;
            _workerMessageHandler = null;
            if (ipcServer is not null)
            {
                if (messageHandler is not null)
                {
                    ipcServer.OnMessageReceived -= messageHandler;
                }
                ipcServer.Dispose();
            }
            if (ReferenceEquals(_workerProcess, workerProcess))
            {
                _workerProcess = null;
            }
            WorkerReadiness? readiness = Volatile.Read(ref _workerReadiness);
            readiness?.Stop();
            if (ReferenceEquals(Volatile.Read(ref _workerReadiness), readiness))
            {
                Interlocked.CompareExchange(ref _workerReadiness, null, readiness);
            }
            _workerExitNotification = null;
            if (canDisposeWorker)
            {
                workerProcess?.Dispose();
            }
        }
        finally
        {
            _transitionGate.Release();
            _cts.Dispose();
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
