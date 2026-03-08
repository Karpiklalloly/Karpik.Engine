using System.Diagnostics;

namespace Karpik.Engine.Core;

public class CoreRunner
{
    private ProcessManager? _processManager;
    private volatile bool _hotReloadInProgress = false;
    
    private static IpcClient? _ipcClient;
    private static Bootstrap _bootstrap;
    private static Ref<bool> _isRunning = new(true);
    private static HotReloadState? _initialState;
    private static volatile bool _stateCollected = false;
    
    public void Start(Ref<bool> isRunning, Side side)
    {
        Console.WriteLine("[Watcher] Starting...");

#if SUPER_HOT_RELOAD
        _processManager = new ProcessManager(side);
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
        _processManager?.StopWorkerAsync().Wait();
        _processManager?.Dispose();
        
        Console.WriteLine("[Watcher] Exited");
#else
        RunEngine(side);
#endif
        
        
    }
    
    internal ProcessManager? GetProcessManager() => _processManager;
    
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
    
    private static void RunEngine(Side side)
    {
        _bootstrap = new Bootstrap(side);
        var loader = new ModuleLoader();
        loader.LoadClientModules();
        var types = GetTypes(loader);
        _bootstrap.RegisterTypes(types);
        
        Dictionary<string, byte[]>? initialHotReloadData = null;
        if (_initialState != null && _initialState.ModuleStates.Count > 0)
        {
            initialHotReloadData = _initialState.ModuleStates;
            Console.WriteLine($"[Worker] Will apply hot reload state from {_initialState.ModuleStates.Count} modules");
        }
        
        Console.WriteLine(Environment.CurrentManagedThreadId);
        var mainThreadScheduler = _bootstrap.Initialize(Environment.CurrentManagedThreadId, _isRunning, initialHotReloadData);

        switch (side)
        {
            case Side.Client:
                ClientLoop(mainThreadScheduler);
                break;
            case Side.Server:
                ServerLoop(mainThreadScheduler);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }
        
        _bootstrap.Shutdown();
        
        Console.WriteLine("[Worker] Exited cleanly");
    }
    
    private static Type[] GetTypes(ModuleLoader loader)
    {
        return loader.LoadedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .ToArray();
    }
    
    private static void ServerLoop(MainThreadScheduler mainThreadScheduler)
    {
        var stopwatch = Stopwatch.StartNew();
        double nextTickTime = stopwatch.Elapsed.TotalSeconds;
        
        while (_isRunning.Value)
        {
            double currentTime = stopwatch.Elapsed.TotalSeconds;
            int loops = 0;
            
            while (currentTime >= nextTickTime && loops < 5)
            {
                mainThreadScheduler.Execute();
                _bootstrap.Loop(Application.TICK_DT);
                nextTickTime += Application.TICK_DT;
                loops++;
            }
            
            if (loops >= 5)
            {
                Console.WriteLine($"Server overloading! Skipping ticks. Lag: {currentTime - nextTickTime:F4}s");
                nextTickTime = currentTime + Application.TICK_DT;
            }
            
            double timeToSleep = nextTickTime - stopwatch.Elapsed.TotalSeconds;
            if (timeToSleep > 0.001)
            {
                int sleepMs = (int)(timeToSleep * 1000);
                Thread.Sleep(sleepMs);
            }
            else
            {
                Thread.Yield(); 
            }
        }
    }

    private static void ClientLoop(MainThreadScheduler mainThreadScheduler)
    {
        var stopwatch = Stopwatch.StartNew();
        double lastTime = 0;
        
        while (_isRunning.Value)
        {
            double currentTime = stopwatch.Elapsed.TotalSeconds;
            double deltaTime = currentTime - lastTime;
            if (deltaTime > 0.1) deltaTime = 0.1;
            
            mainThreadScheduler.Execute();
            
            if (_stateCollected)
            {
                break;
            }
            
            _bootstrap.Loop(deltaTime);
            
            lastTime = currentTime;
        }
    }
}