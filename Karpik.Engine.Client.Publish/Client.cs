using System.Diagnostics;
using System.Reflection;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Hot;
using Karpik.Engine.Core.ProcessManagement;

namespace Karpik.Engine.Client.Publish;

public class Client
{
    private ProcessManager? _processManager;
    private Timer? _debounceTimer;
    private readonly Lock _lock = new();
    private volatile bool _hotReloadInProgress = false;
    
    public void Start(Ref<bool> isRunning)
    {
        Console.WriteLine("[Watcher] Starting...");
        
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
        
        _processManager.StartWorkerAsync().Wait();
        
        Console.WriteLine("[Watcher] Press 'Q' to quit");
        
        while (isRunning.Value && (_processManager.IsWorkerRunning || _processManager.IsWorkerReady || _hotReloadInProgress))
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.Q)
                {
                    Console.WriteLine("[Watcher] Quit requested by user");
                    isRunning.Value = false;
                }
            }
            
            // Just wait for worker to exit or hot reload request
            Thread.Sleep(100);
        }
        
        // Cleanup
        Console.WriteLine("[Watcher] Shutting down...");
        _debounceTimer?.Dispose();
        _processManager?.StopWorkerAsync().Wait();
        _processManager?.Dispose();
        
        Console.WriteLine("[Watcher] Exited");
    }
    
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
