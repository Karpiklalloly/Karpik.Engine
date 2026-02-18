using System.Diagnostics;
using System.Reflection;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Hot;
using Karpik.Engine.Core.ModuleManagement;
using Karpik.Engine.Core.ProcessManagement;

namespace Karpik.Engine.Core.Runner;

/// <summary>
/// Worker process entry point.
/// This process loads modules and runs the game engine.
/// It can be killed and restarted by the Watcher process for hot reload.
/// </summary>
public class Program
{
    private static IpcClient? _ipcClient;
    private static Bootstrap? _bootstrap;
    private static Ref<bool> _isRunning = new(true);
    private static HotReloadState? _initialState;
    private static volatile bool _stateCollected = false;
    
    /// <summary>
    /// Event that can be triggered to request hot reload from the watcher.
    /// </summary>
    public static event Action? OnHotReloadRequested;
    
    public static void Main(string[] args)
    {
        Console.WriteLine("[Worker] Starting...");
        
        // Parse arguments
        var pipeName = ParseArg(args, "--pipe-name");
        var stateBase64 = ParseArg(args, "--state");
        var waitForDebugger = HasArg(args, "--wait-for-debugger");
        
        // Wait for debugger if requested
        if (waitForDebugger)
        {
            Console.WriteLine("[Worker] Waiting for debugger to attach...");
            while (!Debugger.IsAttached)
            {
                Thread.Sleep(100);
            }
            Console.WriteLine("[Worker] Debugger attached!");
        }
        
        // Deserialize initial state if provided
        if (!string.IsNullOrEmpty(stateBase64))
        {
            try
            {
                var stateBytes = Convert.FromBase64String(stateBase64);
                _initialState = HotReloadState.Deserialize(stateBytes);
                Console.WriteLine($"[Worker] Loaded initial state with {_initialState.ModuleStates.Count} modules");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Worker] Failed to deserialize initial state: {ex.Message}");
            }
        }
        
        // Connect to watcher via IPC
        if (!string.IsNullOrEmpty(pipeName))
        {
            _ipcClient = new IpcClient(pipeName);
            
            // Set up callbacks
            _ipcClient.OnStateRequest = GetHotReloadState;
            _ipcClient.OnShutdownRequest = () =>
            {
                Console.WriteLine("[Worker] Shutdown requested");
                _isRunning.Value = false;
            };
            
            try
            {
                _ipcClient.ConnectAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Worker] Failed to connect to watcher: {ex.Message}");
                Console.WriteLine("[Worker] Running in standalone mode (no IPC)");
                _ipcClient = null;
            }
        }
        else
        {
            Console.WriteLine("[Worker] No pipe name provided, running in standalone mode");
        }
        
        // Initialize and run the engine
        try
        {
            RunEngine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Worker] Engine crashed: {ex}");
            throw;
        }
        finally
        {
            _ipcClient?.Dispose();
        }
    }
    
    private static void RunEngine()
    {
        _bootstrap = new Bootstrap();
        
        // Subscribe HotReloadHandler to IPC hot reload request
        // This allows DebugThings.HotReload() to trigger process-isolation hot reload
        HotReloadHandler.OnUpdateApplication += RequestHotReload;
        
        // Load modules using ModuleLoader
        var loader = new ModuleLoader();
        loader.LoadClientModules();
        var types = GetTypes(loader);
        _bootstrap.RegisterTypes(types);
        
        // Get initial hot reload state if available
        Dictionary<string, byte[]>? initialHotReloadData = null;
        if (_initialState != null && _initialState.ModuleStates.Count > 0)
        {
            initialHotReloadData = _initialState.ModuleStates;
            Console.WriteLine($"[Worker] Will apply hot reload state from {_initialState.ModuleStates.Count} modules");
        }
        
        // Initialize bootstrap (passing initial state for process-isolation hot reload)
        Console.WriteLine(Environment.CurrentManagedThreadId);
        var mainThreadScheduler = _bootstrap.Initialize(Environment.CurrentManagedThreadId, _isRunning, initialHotReloadData);
        
        // Set the scheduler on IPC client so state collection runs on main thread
        _ipcClient?.SetScheduler(mainThreadScheduler);
        
        // Notify watcher that we're ready
        _ipcClient?.SendReadyAsync().Wait();
        
        // Main loop
        var stopwatch = Stopwatch.StartNew();
        double lastTime = 0;
        
        while (_isRunning.Value)
        {
            double currentTime = stopwatch.Elapsed.TotalSeconds;
            double deltaTime = currentTime - lastTime;
            if (deltaTime > 0.1) deltaTime = 0.1;
            
            mainThreadScheduler.Execute();
            
            // Check if state was collected during Execute() - if so, skip Loop() and exit
            if (_stateCollected)
            {
                break;
            }
            
            _bootstrap.Loop(deltaTime);
            
            lastTime = currentTime;
        }
        
        // Shutdown
        HotReloadHandler.OnUpdateApplication -= RequestHotReload;
        _bootstrap.Shutdown();
        
        Console.WriteLine("[Worker] Exited cleanly");
    }
    
    private static HotReloadState? GetHotReloadState()
    {
        if (_bootstrap == null)
        {
            Console.WriteLine("[Worker] Cannot collect hot reload state: bootstrap not initialized");
            return null;
        }
        
        Console.WriteLine("[Worker] Collecting hot reload state...");
        
        try
        {
            var moduleData = _bootstrap.GetHotReloadData();
            
            var state = new HotReloadState
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            // Copy module states into the HotReloadState
            foreach (var (moduleName, data) in moduleData)
            {
                state.ModuleStates[moduleName] = data;
                Console.WriteLine($"[Worker] Collected state from module: {moduleName} ({data.Length} bytes)");
            }
            
            Console.WriteLine($"[Worker] Total modules with state: {state.ModuleStates.Count}");
            
            // Signal that state was collected - main loop should exit immediately
            // without running Loop() again (worlds are destroyed)
            _stateCollected = true;
            _isRunning.Value = false;
            
            return state;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Worker] Failed to collect hot reload state: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Requests hot reload from the watcher. Can be called by modules to trigger a reload.
    /// </summary>
    public static void RequestHotReload()
    {
        if (_ipcClient == null)
        {
            Console.WriteLine("[Worker] Cannot request hot reload: IPC not connected");
            return;
        }
        
        Console.WriteLine("[Worker] Requesting hot reload from watcher...");
        _ = _ipcClient.RequestHotReloadAsync();
    }
    
    private static Type[] GetTypes(ModuleLoader loader)
    {
        return loader.LoadedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .ToArray();
    }
    
    private static string? ParseArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith($"{name}="))
            {
                return args[i].Substring(name.Length + 1);
            }
        }
        return null;
    }
    
    private static bool HasArg(string[] args, string name)
    {
        return args.Any(arg => arg == name || arg.StartsWith($"{name}="));
    }
}
