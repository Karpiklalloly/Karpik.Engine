using System.Diagnostics;

namespace Karpik.Engine.Core.ProcessManagement;

/// <summary>
/// Manages the worker process lifecycle.
/// Used by the Watcher process to spawn, monitor, and restart the Worker.
/// </summary>
public class ProcessManager : IDisposable
{
    private Process? _workerProcess;
    private IpcServer? _ipcServer;
    private readonly string _workerExePath;
    private readonly string _pipeName;
    private HotReloadState? _pendingState;
    
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitorTask;
    
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
    
    public int WorkerProcessId => _workerProcess?.Id ?? -1;
    
    public ProcessManager(string? workerExePath = null, string? pipeName = null)
    {
        _workerExePath = workerExePath ?? GetDefaultWorkerPath();
        _pipeName = pipeName ?? $"KarpikEngine_{Guid.NewGuid():N}";
    }
    
    /// <summary>
    /// Gets the pipe name that the worker should connect to.
    /// </summary>
    public string GetPipeName() => _pipeName;
    
    /// <summary>
    /// Starts the worker process.
    /// </summary>
    public async Task StartWorkerAsync(HotReloadState? initialState = null, CancellationToken cancellationToken = default)
    {
        if (IsWorkerRunning)
        {
            Console.WriteLine("[ProcessManager] Worker is already running");
            return;
        }
        
        _pendingState = initialState;
        
        // Start IPC server first
        _ipcServer = new IpcServer(_pipeName);
        var ipcTask = _ipcServer.WaitForConnectionAsync(cancellationToken);
        
        // Build arguments
        var args = $"--pipe-name={_pipeName}";
        if (initialState != null)
        {
            var stateBase64 = Convert.ToBase64String(initialState.Serialize());
            args += $" --state={stateBase64}";
        }
        
        // Spawn worker process
        Console.WriteLine($"[ProcessManager] Starting worker: {_workerExePath}");
        Console.WriteLine($"[ProcessManager] Arguments: {args}");
        
        _workerProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _workerExePath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                WorkingDirectory = Path.GetDirectoryName(_workerExePath) ?? AppContext.BaseDirectory
            },
            EnableRaisingEvents = true
        };
        
        _workerProcess.Exited += (sender, e) =>
        {
            var exitCode = _workerProcess.ExitCode;
            Console.WriteLine($"[ProcessManager] Worker process exited with code: {exitCode}");
            OnWorkerExited?.Invoke(exitCode);
        };
        
        if (!_workerProcess.Start())
        {
            throw new InvalidOperationException($"Failed to start worker process: {_workerExePath}");
        }
        
        Console.WriteLine($"[ProcessManager] Worker started with PID: {_workerProcess.Id}");
        
        // Wait for IPC connection
        await ipcTask;
        
        // Set up message handlers
        _ipcServer.OnMessageReceived += msg =>
        {
            if (msg.Type == IpcMessageType.WorkerReady)
            {
                Console.WriteLine("[ProcessManager] Worker is ready");
                OnWorkerReady?.Invoke();
            }
            else if (msg.Type == IpcMessageType.HotReloadRequest)
            {
                Console.WriteLine("[ProcessManager] Worker requested hot reload");
                _ = HotReloadAsync();
            }
        };
        
        // Start monitoring task
        _monitorTask = MonitorLoop(_cts.Token);
    }
    
    /// <summary>
    /// Requests hot reload: gets state from worker, shuts it down, restarts with state.
    /// </summary>
    public async Task HotReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_ipcServer == null || !IsWorkerRunning)
        {
            Console.WriteLine("[ProcessManager] Cannot hot reload: worker not running");
            return;
        }
        
        Console.WriteLine("[ProcessManager] Starting hot reload...");
        
        // 1. Request state from worker
        var state = await _ipcServer.RequestStateAsync(cancellationToken);
        OnHotReloadRequested?.Invoke(state);
        
        // 2. Request graceful shutdown
        await _ipcServer.SendShutdownRequestAsync(cancellationToken);
        
        // 3. Wait for process to exit (with timeout)
        var timeout = TimeSpan.FromSeconds(5);
        if (!await WaitForExitAsync(timeout))
        {
            Console.WriteLine("[ProcessManager] Worker didn't exit gracefully, killing...");
            _workerProcess?.Kill(entireProcessTree: true);
        }
        
        // 4. Cleanup
        _ipcServer.Dispose();
        _ipcServer = null;
        _workerProcess?.Dispose();
        _workerProcess = null;
        
        // 5. Restart with state
        await StartWorkerAsync(state, cancellationToken);
        
        Console.WriteLine("[ProcessManager] Hot reload complete!");
    }
    
    /// <summary>
    /// Stops the worker process gracefully.
    /// </summary>
    public async Task StopWorkerAsync(CancellationToken cancellationToken = default)
    {
        if (!IsWorkerRunning)
        {
            return;
        }
        
        Console.WriteLine("[ProcessManager] Stopping worker...");
        
        if (_ipcServer != null && _ipcServer.IsConnected)
        {
            await _ipcServer.SendShutdownRequestAsync(cancellationToken);
            
            if (!await WaitForExitAsync(TimeSpan.FromSeconds(5)))
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
    
    /// <summary>
    /// Waits for the worker process to exit.
    /// </summary>
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
            // 1. Same directory as the watcher (most common for published apps)
            AppContext.BaseDirectory,
            // 2. Sibling directory relative to solution (for development)
            Path.Combine(AppContext.BaseDirectory, "..", "Karpik.Engine.Core.Runner"),
            // 3. Look in common build output directories
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Karpik.Engine.Core.Runner", "bin", "Debug", "net10.0"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Karpik.Engine.Core.Runner", "bin", "Release", "net10.0"),
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
        
        // Fallback: return the first search path (will fail with clear error message)
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, exeName);
        Console.WriteLine($"[ProcessManager] Worker not found in any search location. Expected at: {fallbackPath}");
        Console.WriteLine("[ProcessManager] Make sure Karpik.Engine.Core.Runner is built and copied to the output directory.");
        return fallbackPath;
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
