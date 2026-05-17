using System.Diagnostics;

namespace Karpik.Engine.Core;

public class CoreRunner
{
    private ProcessManager? _processManager;
    private static Bootstrap _bootstrap = null!;
    private static Ref<bool> _isRunning = new(true);
    private static volatile bool _stateCollected = false;
    
    public void Start(Ref<bool> isRunning, Side side) => Start(isRunning, side, HotReloadOptions.Default);

    public void Start(Ref<bool> isRunning, Side side, HotReloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(isRunning);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Mode == HotReloadMode.RestartWorker)
        {
            RunWithWorkerRestart(isRunning, side, options);
            return;
        }

        RunEngine(isRunning, side);
    }
    
    internal ProcessManager? GetProcessManager() => _processManager;

    private void RunWithWorkerRestart(Ref<bool> isRunning, Side side, HotReloadOptions options)
    {
        Console.WriteLine("[Watcher] Starting restart-worker hot reload...");

        _processManager = new ProcessManager(side, options);
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

        while (isRunning.Value
               && (_processManager.IsWorkerRunning
                   || _processManager.IsWorkerReady
                   || _processManager.IsReloadInProgress))
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

            Thread.Sleep(100);
        }

        Console.WriteLine("[Watcher] Shutting down...");
        _processManager?.StopWorkerAsync().Wait();
        _processManager?.Dispose();

        Console.WriteLine("[Watcher] Exited");
    }

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

    private static void RunEngine(Ref<bool> isRunning, Side side)
    {
        _isRunning = isRunning;
        _stateCollected = false;
        _bootstrap = new Bootstrap(side);
        var loader = new ModuleLoader();
        switch (side)
        {
            case Side.Client:
                loader.LoadClientModules();
                break;
            case Side.Server:
                loader.LoadServerModules();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }
        var types = GetTypes(loader);
        _bootstrap.RegisterTypes(types);
        
        Console.WriteLine(Environment.CurrentManagedThreadId);
        var mainThreadScheduler = _bootstrap.Initialize(Environment.CurrentManagedThreadId, _isRunning);

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
