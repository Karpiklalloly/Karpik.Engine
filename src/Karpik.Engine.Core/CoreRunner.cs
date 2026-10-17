using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core;

public class CoreRunner
{
    private ProcessManager? _processManager;
    private static Bootstrap _bootstrap = null!;
    private static Ref<bool> _isRunning = new(true);
    private static volatile bool _stateCollected = false;
    private ILogger<CoreRunner> _logger = null!;
    
    public void Start(Ref<bool> isRunning, Side side) => Start(isRunning, side, HotReloadOptions.Default);

    public void Start(Ref<bool> isRunning, Side side, HotReloadOptions options)
    {
        using ILoggerFactory loggerFactory = HostLogging.CreateDefaultFactory();
        Start(isRunning, side, options, loggerFactory);
    }

    public void Start(Ref<bool> isRunning, Side side, HotReloadOptions options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(isRunning);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<CoreRunner>();

        try
        {
            if (options.Mode == HotReloadMode.RestartWorker)
            {
                RunWithWorkerRestart(isRunning, side, options, loggerFactory);
                return;
            }

            RunEngine(isRunning, side, loggerFactory);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Engine host failed");
            throw;
        }
    }
    
    internal ProcessManager? GetProcessManager() => _processManager;

    private void RunWithWorkerRestart(Ref<bool> isRunning, Side side, HotReloadOptions options, ILoggerFactory loggerFactory)
    {
        _logger.LogInformation("Starting restart-worker hot reload");

        _processManager = new ProcessManager(side, options, null, loggerFactory);
        _processManager.OnWorkerExited += (exitCode) =>
        {
            _logger.LogInformation("Worker exited with code {ExitCode}", exitCode);
            if (exitCode != 0 && isRunning.Value)
            {
                _logger.LogWarning("Worker crashed, restarting");
                _ = RestartWorkerAsync();
            }
        };

        _processManager.OnWorkerReady += () =>
        {
            _logger.LogInformation("Worker is ready");
        };

        try
        {
            _processManager.StartWorkerAsync().Wait();

            Console.WriteLine("[Watcher] Press 'R' to hot reload, 'Q' to quit");

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
                        _logger.LogInformation("Quit requested by user");
                        isRunning.Value = false;
                    }
                    else if (key == ConsoleKey.R)
                    {
                        _logger.LogInformation("Hot reload requested by user");
                        try
                        {
                            _processManager.HotReloadAsync().GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Hot reload failed");
                        }
                    }
                }

                Thread.Sleep(100);
            }

            _logger.LogInformation("Shutting down");
            _processManager.StopWorkerAsync().Wait();

            _logger.LogInformation("Exited");
        }
        finally
        {
            _processManager?.Dispose();
        }
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
            _logger.LogError(ex, "Failed to restart worker");
        }
    }

    private void RunEngine(Ref<bool> isRunning, Side side, ILoggerFactory loggerFactory)
    {
        _isRunning = isRunning;
        _stateCollected = false;
        _bootstrap = new Bootstrap(side, loggerFactory);
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
        
        _logger.LogInformation("Engine main thread {ThreadId}", Environment.CurrentManagedThreadId);
        var mainThreadScheduler = _bootstrap.Initialize(Environment.CurrentManagedThreadId, _isRunning);
        mainThreadScheduler.Execute();
        _bootstrap.Startup.GetAwaiter().GetResult();

        switch (side)
        {
            case Side.Client:
                ClientLoop(mainThreadScheduler);
                break;
            case Side.Server:
                ServerLoop(mainThreadScheduler, _logger);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }
        
        _bootstrap.ShutdownAsync().GetAwaiter().GetResult();
        
        _logger.LogInformation("Worker exited cleanly");
    }
    
    private static Type[] GetTypes(ModuleLoader loader)
    {
        return loader.LoadedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .ToArray();
    }
    
    private static void ServerLoop(MainThreadScheduler mainThreadScheduler, ILogger logger)
    {
        var stopwatch = Stopwatch.StartNew();
        double nextTickTime = stopwatch.Elapsed.TotalSeconds;
        bool overloadWarned = false;
        
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
                if (!overloadWarned)
                {
                    logger.LogWarning("Server overloading; skipping ticks. Lag: {LagSeconds:F4}s", currentTime - nextTickTime);
                    overloadWarned = true;
                }
                nextTickTime = currentTime + Application.TICK_DT;
            }
            else
            {
                overloadWarned = false;
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
        double lastSimulationRequestTime = 0;
        var metrics = _bootstrap.ClientFrameMetrics;
        using var simulationWorker = new ClientSimulationWorker(_bootstrap, metrics);
        
        while (_isRunning.Value)
        {
            long frameStartedAt = Stopwatch.GetTimestamp();
            double currentTime = stopwatch.Elapsed.TotalSeconds;
            
            mainThreadScheduler.Execute();
            
            if (_stateCollected)
            {
                break;
            }

            simulationWorker.ThrowIfFaulted();
            _bootstrap.RunMainThreadBegin();

            if (!simulationWorker.IsSimulationRunning)
            {
                _isRunning.Value = false;
                break;
            }

            if (simulationWorker.TryReserveFrame())
            {
                try
                {
                    _bootstrap.RunMainThreadFrameBegin();
                    double deltaTime = Math.Min(currentTime - lastSimulationRequestTime, 0.1);
                    simulationWorker.StartReservedFrame(deltaTime);
                    lastSimulationRequestTime = currentTime;
                }
                catch
                {
                    simulationWorker.CancelReservedFrame();
                    throw;
                }
            }

            _bootstrap.RunRender();
            metrics.PublishMainThreadFrame(Stopwatch.GetTimestamp() - frameStartedAt);
        }
    }

}

internal static class HostLogging
{
    internal static ILoggerFactory CreateDefaultFactory() =>
        LoggerFactory.Create(logging => logging.AddSimpleConsole());
}
