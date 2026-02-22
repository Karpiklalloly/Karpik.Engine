using System.Diagnostics;

namespace Karpik.Engine.Core;

internal class ProcessManager : IDisposable
{
    private Process? _workerProcess;
    private IpcServer? _ipcServer;
    private readonly string _workerExePath;
    private readonly string _pipeName;
    private HotReloadState? _pendingState;
    private readonly Side _side;
    private readonly bool _waitForDebugger;
    
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
    
    public int WorkerProcessId => _workerProcess?.Id ?? -1;
    
    public ProcessManager(Side side, string? workerExePath = null, string? pipeName = null, bool waitForDebugger = true)
    {
        _workerExePath = workerExePath ?? GetDefaultWorkerPath();
        _pipeName = pipeName ?? $"KarpikEngine_{Guid.NewGuid():N}";
        _side = side;
        _waitForDebugger = waitForDebugger;
    }
    
    public string GetPipeName() => _pipeName;
    
    public async Task StartWorkerAsync(HotReloadState? initialState = null, CancellationToken cancellationToken = default)
    {
        if (IsWorkerRunning)
        {
            Console.WriteLine("[ProcessManager] Worker is already running");
            return;
        }
        
        _pendingState = initialState;
        IsWorkerReady = false;
        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        _ipcServer = new IpcServer(_pipeName);
        var ipcTask = _ipcServer.WaitForConnectionAsync(cancellationToken);
        
        var args = $"--pipe-name={_pipeName}";
        if (initialState != null)
        {
            var stateBase64 = Convert.ToBase64String(initialState.Serialize());
            args += $" --state={stateBase64}";
        }
        if (_waitForDebugger)
        {
            args += " --wait-for-debugger";
        }

        args += $" --side={_side}";
        
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
        
        var capturedProcess = _workerProcess;
        capturedProcess.Exited += (sender, e) =>
        {
            var exitCode = capturedProcess.ExitCode;
            Console.WriteLine($"[ProcessManager] Worker process exited with code: {exitCode}");
            _readyTcs?.TrySetCanceled();
            OnWorkerExited?.Invoke(exitCode);
        };
        
        if (!_workerProcess.Start())
        {
            throw new InvalidOperationException($"Failed to start worker process: {_workerExePath}");
        }
        
        Console.WriteLine($"[ProcessManager] Worker started with PID: {_workerProcess.Id}");
        
        await ipcTask;
        
        _ipcServer.OnMessageReceived += msg =>
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
        };
        
        _monitorTask = MonitorLoop(_cts.Token);
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
        if (_ipcServer == null || !IsWorkerRunning)
        {
            Console.WriteLine("[ProcessManager] Cannot hot reload: worker not running");
            return;
        }
        
        Console.WriteLine("[ProcessManager] Starting hot reload...");
        
        var state = await _ipcServer.RequestStateAsync(cancellationToken);
        OnHotReloadRequested?.Invoke(state);
        
        await _ipcServer.SendShutdownRequestAsync(cancellationToken);
        
        var timeout = TimeSpan.FromSeconds(5);
        if (!await WaitForExitAsync(timeout))
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
