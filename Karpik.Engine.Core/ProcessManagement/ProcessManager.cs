using System.Diagnostics;

namespace Karpik.Engine.Core;

internal class ProcessManager : IDisposable
{
    private Process? _workerProcess;
    private IpcServer? _ipcServer;
    private readonly string _workerExePath;
    private readonly string _pipeName;
    private readonly Side _side;
    private readonly HotReloadOptions _options;
    private bool _hasStartedWorker;
    private bool _reloadInProgress;
    
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitorTask;
    private TaskCompletionSource<bool>? _readyTcs;
    
    public event Action<int>? OnWorkerExited;
    public event Action? OnWorkerReady;
    public event Action<HotReloadState?>? OnHotReloadRequested;
    
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

    public bool IsReloadInProgress => _reloadInProgress;
    
    public int WorkerProcessId => _workerProcess?.Id ?? -1;
    
    public ProcessManager(Side side, HotReloadOptions options, string? pipeName = null)
    {
        _options = options;
        _workerExePath = options.WorkerExecutablePath ?? GetDefaultWorkerPath();
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
        
        _ipcServer = new IpcServer(_pipeName);
        _ipcServer.OnMessageReceived += HandleWorkerMessage;
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectionCts.CancelAfter(_options.WorkerConnectionTimeout);
        var ipcTask = _ipcServer.WaitForConnectionAsync(connectionCts.Token);
        
        var arguments = new List<string>
        {
            $"--pipe-name={_pipeName}",
            $"--side={_side}"
        };

        if (initialState != null)
        {
            arguments.Add($"--state-file={WriteStateFile(initialState)}");
        }
        var shouldWaitForDebugger = _hasStartedWorker
            ? _options.WaitForDebuggerOnReloadWorkerStart
            : _options.WaitForDebuggerOnInitialWorkerStart;

        if (shouldWaitForDebugger)
        {
            arguments.Add("--wait-for-debugger");
        }
        
        Console.WriteLine($"[ProcessManager] Starting worker: {_workerExePath}");
        Console.WriteLine($"[ProcessManager] Arguments: {string.Join(" ", arguments)}");
        
        var startInfo = new ProcessStartInfo
        {
            FileName = _workerExePath,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = Path.GetDirectoryName(_workerExePath) ?? AppContext.BaseDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        _workerProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        
        var capturedProcess = _workerProcess;
        capturedProcess.Exited += (sender, e) =>
        {
            var exitCode = capturedProcess.ExitCode;
            Console.WriteLine($"[ProcessManager] Worker process exited with code: {exitCode}");
            IsWorkerReady = false;
            _readyTcs?.TrySetCanceled();
            OnWorkerExited?.Invoke(exitCode);
        };
        
        if (!_workerProcess.Start())
        {
            throw new InvalidOperationException($"Failed to start worker process: {_workerExePath}");
        }
        
        Console.WriteLine($"[ProcessManager] Worker started with PID: {_workerProcess.Id}");
        
        try
        {
            await ipcTask;
        }
        catch
        {
            if (IsWorkerRunning)
            {
                _workerProcess?.Kill(entireProcessTree: true);
            }

            _ipcServer.Dispose();
            _ipcServer = null;
            _workerProcess?.Dispose();
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
    
    public async Task<bool> WaitForWorkerReadyAsync(TimeSpan timeout)
    {
        if (IsWorkerReady) return true;
        if (_readyTcs == null) return false;
        
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_readyTcs.Task, timeoutTask);
        return completedTask == _readyTcs.Task && IsWorkerReady;
    }

    public async Task HotReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_reloadInProgress)
        {
            Console.WriteLine("[ProcessManager] Hot reload is already in progress");
            return;
        }

        if (_ipcServer == null || !IsWorkerRunning)
        {
            Console.WriteLine("[ProcessManager] Cannot hot reload: worker not running");
            return;
        }

        _reloadInProgress = true;
        try
        {
            Console.WriteLine("[ProcessManager] Starting hot reload...");

            var (receivedState, state) = await _ipcServer.TryRequestStateAsync(
                _options.StateRequestTimeout,
                cancellationToken);

            if (!receivedState || state == null)
            {
                Console.WriteLine("[ProcessManager] Hot reload aborted: failed to collect ECS state. Existing worker remains running.");
                return;
            }

            OnHotReloadRequested?.Invoke(state);

            await _ipcServer.SendShutdownRequestAsync(_options.GracefulShutdownTimeout, cancellationToken);

            if (!await WaitForExitAsync(_options.GracefulShutdownTimeout))
            {
                Console.WriteLine("[ProcessManager] Worker didn't exit gracefully, killing...");
                _workerProcess?.Kill(entireProcessTree: true);
            }

            _ipcServer.Dispose();
            _ipcServer = null;
            _workerProcess?.Dispose();
            _workerProcess = null;

            await StartWorkerAsync(state, cancellationToken);

            Console.WriteLine("[ProcessManager] Hot reload complete!");
        }
        finally
        {
            _reloadInProgress = false;
        }
    }

    public async Task StopWorkerAsync(CancellationToken cancellationToken = default)
    {
        if (!IsWorkerRunning)
        {
            return;
        }
        
        Console.WriteLine("[ProcessManager] Stopping worker...");
        
        if (_ipcServer != null && _ipcServer.IsConnected)
        {
            await _ipcServer.SendShutdownRequestAsync(_options.GracefulShutdownTimeout, cancellationToken);
            
            if (!await WaitForExitAsync(_options.GracefulShutdownTimeout))
            {
                Console.WriteLine("[ProcessManager] Worker didn't exit gracefully, killing...");
                _workerProcess?.Kill(entireProcessTree: true);
            }
        }
        else
        {
            _workerProcess?.Kill(entireProcessTree: true);
        }
        
        _ipcServer?.Dispose();
        _ipcServer = null;
        _workerProcess?.Dispose();
        _workerProcess = null;
    }
    
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        if (_workerProcess == null) return true;
        
        var tcs = new TaskCompletionSource<bool>();
        
        void Handler(object? sender, EventArgs e)
        {
            _workerProcess.Exited -= Handler;
            tcs.SetResult(true);
        }
        
        _workerProcess.Exited += Handler;
        
        if (_workerProcess.HasExited)
        {
            _workerProcess.Exited -= Handler;
            return true;
        }
        
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
        
        return completedTask == tcs.Task;
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

    private static string WriteStateFile(HotReloadState state)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "reload", "state");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, state.Serialize());
        return path;
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        
        try
        {
            if (IsWorkerRunning)
            {
                _workerProcess?.Kill(entireProcessTree: true);
            }
        }
        catch { }
        
        _ipcServer?.Dispose();
        _workerProcess?.Dispose();
        _cts.Dispose();
    }
}
