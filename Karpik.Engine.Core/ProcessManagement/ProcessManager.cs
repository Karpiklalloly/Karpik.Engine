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
    
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitorTask;
    private TaskCompletionSource<bool>? _readyTcs;
    
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
    
    public bool IsWorkerReady { get; private set; }

    public bool IsReloadInProgress => Volatile.Read(ref _reloadInProgress) != 0;
    
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

        IsWorkerReady = false;
        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ipcServer?.Dispose();
        
        _ipcServer = new IpcServer(_pipeName);
        _ipcServer.OnMessageReceived += HandleWorkerMessage;
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectionCts.CancelAfter(_options.WorkerConnectionTimeout);
        var ipcTask = _ipcServer.WaitForConnectionAsync(connectionCts.Token);
        
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

        Console.WriteLine($"[ProcessManager] Starting worker: {_workerExePath}");
        Console.WriteLine($"[ProcessManager] Arguments: {string.Join(" ", startInfo.ArgumentList)}");

        _workerProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        
        var capturedProcess = _workerProcess;
        if (_options.CaptureWorkerOutput)
        {
            capturedProcess.OutputDataReceived += (_, args) => PublishWorkerOutput(args.Data, isError: false);
            capturedProcess.ErrorDataReceived += (_, args) => PublishWorkerOutput(args.Data, isError: true);
        }

        capturedProcess.Exited += (sender, e) =>
        {
            var exitCode = capturedProcess.ExitCode;
            Console.WriteLine($"[ProcessManager] Worker process exited with code: {exitCode}");
            IsWorkerReady = false;
            _readyTcs?.TrySetCanceled();
            CleanupWorkerShadowCopies(capturedProcess.Id);
            OnWorkerExited?.Invoke(exitCode);
        };
        
        if (!_workerProcess.Start())
        {
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

            _ipcServer.Dispose();
            _ipcServer = null;
            failedProcess?.Dispose();
            _workerProcess = null;
            throw;
        }

        _hasStartedWorker = true;
        _monitorTask = MonitorLoop(_cts.Token);

        void HandleWorkerMessage(IpcMessage msg)
        {
            if (msg.Type == IpcMessageType.WorkerReady)
            {
                Console.WriteLine("[ProcessManager] Worker is ready");
                IsWorkerReady = true;
                CleanupCompletedModuleVersions(msg.Payload);
                _readyTcs?.TrySetResult(true);
                OnWorkerReady?.Invoke();
            }
            else if (msg.Type == IpcMessageType.HotReloadRequest)
            {
                Console.WriteLine("[ProcessManager] Worker requested hot reload");
                _ = HotReloadAsync(cancellationToken);
            }
        }
    }
    
    public async Task<bool> WaitForWorkerReadyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (IsWorkerReady) return true;
        if (_readyTcs == null) return false;

        Task readyTask = _readyTcs.Task;
        Task timeoutTask = Task.Delay(timeout, cancellationToken);
        Task completedTask = await Task.WhenAny(readyTask, timeoutTask);
        if (completedTask == timeoutTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
        await readyTask;
        return IsWorkerReady;
    }

    public async Task HotReloadAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBeginReload())
        {
            Console.WriteLine("[ProcessManager] Hot reload is already in progress");
            return;
        }

        try
        {
            IpcServer? ipcServer = _ipcServer;
            Process? workerProcess = _workerProcess;
            if (ipcServer is null || workerProcess is null || !IsProcessRunning(workerProcess))
            {
                Console.WriteLine("[ProcessManager] Cannot hot reload: worker not running");
                return;
            }

            Console.WriteLine("[ProcessManager] Starting hot reload...");

            var (receivedState, state) = await ipcServer.TryRequestStateAsync(
                _options.StateRequestTimeout,
                cancellationToken);

            if (!receivedState || state == null)
            {
                Console.WriteLine("[ProcessManager] Hot reload aborted: failed to collect ECS state. Existing worker remains running.");
                return;
            }

            OnHotReloadRequested?.Invoke(state);

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

            ipcServer.Dispose();
            if (ReferenceEquals(_ipcServer, ipcServer))
            {
                _ipcServer = null;
            }
            workerProcess.Dispose();
            if (ReferenceEquals(_workerProcess, workerProcess))
            {
                _workerProcess = null;
            }

            await StartWorkerAsync(state, CancellationToken.None);

            Console.WriteLine("[ProcessManager] Hot reload complete!");
        }
        finally
        {
            EndReload();
        }
    }

    public async Task StopWorkerAsync(CancellationToken cancellationToken = default)
    {
        Process? workerProcess = _workerProcess;
        if (workerProcess is null || !IsProcessRunning(workerProcess))
        {
            return;
        }
        
        Console.WriteLine("[ProcessManager] Stopping worker...");
        
        IpcServer? ipcServer = _ipcServer;
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
        
        int workerProcessId = workerProcess.Id;
        ipcServer?.Dispose();
        if (ReferenceEquals(_ipcServer, ipcServer))
        {
            _ipcServer = null;
        }
        CleanupWorkerShadowCopies(workerProcessId);
        workerProcess.Dispose();
        if (ReferenceEquals(_workerProcess, workerProcess))
        {
            _workerProcess = null;
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
    
    public void Dispose()
    {
        _cts.Cancel();
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
        
        _ipcServer?.Dispose();
        if (canDisposeWorker)
        {
            workerProcess?.Dispose();
        }
        _cts.Dispose();
    }
}
