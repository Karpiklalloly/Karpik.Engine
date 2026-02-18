using System.Diagnostics;
using System.Reflection;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Hot;
using Karpik.Engine.Core.ProcessManagement;

namespace Karpik.Engine.Client.Publish;

/// <summary>
/// Watcher process entry point.
/// Spawns and manages the worker process that runs the actual engine.
/// </summary>
public class Client
{
    private ProcessManager? _processManager;
    private FileSystemWatcher? _fileWatcher;
    private Timer? _debounceTimer;
    private readonly object _lock = new();
    
    public void Start(Ref<bool> isRunning)
    {
        Console.WriteLine("[Watcher] Starting...");
        
        // Create process manager
        _processManager = new ProcessManager();
        _processManager.OnWorkerExited += (exitCode) =>
        {
            Console.WriteLine($"[Watcher] Worker exited with code: {exitCode}");
            if (exitCode != 0 && isRunning.Value)
            {
                Console.WriteLine("[Watcher] Worker crashed, restarting...");
                _ = RestartWorkerAsync();
            }
        };
        
        _processManager.OnWorkerReady += () =>
        {
            Console.WriteLine("[Watcher] Worker is ready!");
        };
        
        // Set up file watcher for hot reload
        SetupFileWatcher();
        
        // Start the worker process
        _processManager.StartWorkerAsync().Wait();
        
        // Main watcher loop
        var stopwatch = Stopwatch.StartNew();
        double lastTime = 0;
        
        Console.WriteLine("[Watcher] Press 'H' to trigger hot reload manually, 'Q' to quit");
        
        while (isRunning.Value && _processManager.IsWorkerRunning)
        {
            double currentTime = stopwatch.Elapsed.TotalSeconds;
            double deltaTime = currentTime - lastTime;
            
            // Check for console input
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.H)
                {
                    Console.WriteLine("[Watcher] Manual hot reload triggered by user");
                    _ = HotReloadAsync();
                }
                else if (key == ConsoleKey.Q)
                {
                    Console.WriteLine("[Watcher] Quit requested by user");
                    isRunning.Value = false;
                }
            }
            
            // Just wait for worker to exit or hot reload request
            Thread.Sleep(100);
            
            lastTime = currentTime;
        }
        
        // Cleanup
        Console.WriteLine("[Watcher] Shutting down...");
        _fileWatcher?.Dispose();
        _debounceTimer?.Dispose();
        _processManager?.StopWorkerAsync().Wait();
        _processManager?.Dispose();
        
        Console.WriteLine("[Watcher] Exited");
    }
    
    private void SetupFileWatcher()
    {
        var path = AppContext.BaseDirectory;
        _fileWatcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            Filter = "*.dll",
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };
        
        _fileWatcher.Changed += OnDllChanged;
        Console.WriteLine($"[Watcher] Watching for DLL changes in: {path}");
    }
    
    private void OnDllChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: wait for 500ms after last change before triggering hot reload
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                Console.WriteLine($"[Watcher] Detected change in {e.Name}, triggering hot reload...");
                _ = HotReloadAsync();
            }, null, 500, Timeout.Infinite);
        }
    }
    
    /// <summary>
    /// Triggers a hot reload manually. Can be called for testing.
    /// </summary>
    public async Task HotReloadAsync()
    {
        if (_processManager == null)
        {
            Console.WriteLine("[Watcher] Cannot hot reload: process manager not initialized");
            return;
        }
        
        try
        {
            Console.WriteLine("[Watcher] Manual hot reload triggered");
            await _processManager.HotReloadAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watcher] Hot reload failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Gets the process manager for direct access.
    /// </summary>
    public ProcessManager? GetProcessManager() => _processManager;
    
    private async Task RestartWorkerAsync()
    {
        if (_processManager == null) return;
        
        try
        {
            await _processManager.StartWorkerAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watcher] Failed to restart worker: {ex.Message}");
        }
    }
}
