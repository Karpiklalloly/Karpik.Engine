using System.Diagnostics;
using Karpik.Engine.Core.Hot;
using Karpik.Engine.Core;

namespace Karpik.Engine.Core.Runner;

public class Program
{
    private static IpcClient? _ipcClient;
    private static Bootstrap _bootstrap = new();
    private static Ref<bool> _isRunning = new(true);
    private static HotReloadState? _initialState;
    private static volatile bool _stateCollected = false;
    
    public static void Main(string[] args)
    {
        Console.WriteLine("[Worker] Starting...");
        
        var pipeName = ParseArg(args, "--pipe-name");
        var stateBase64 = ParseArg(args, "--state");
        var waitForDebugger = HasArg(args, "--wait-for-debugger");
        var side = ParseArg(args, "--side");
        
        if (waitForDebugger)
        {
            Console.WriteLine("[Worker] Waiting for debugger to attach...");
            while (!Debugger.IsAttached)
            {
                Thread.Sleep(100);
            }
            Console.WriteLine("[Worker] Debugger attached!");
        }
        
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
        
        if (!string.IsNullOrEmpty(pipeName))
        {
            _ipcClient = new IpcClient(pipeName);
            
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
        
        try
        {
            var tryParse = Enum.TryParse(side, out Side s);
            RunEngine(s);
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
    
    private static void RunEngine(Side side)
    {
        HotReloadHandler.OnUpdateApplication += RequestHotReload;
        
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
        
        _ipcClient?.SetScheduler(mainThreadScheduler);
        
        _ipcClient?.SendReadyAsync().Wait();

        switch (side)
        {
            case Side.Client:
                ClientLoop(mainThreadScheduler);
                break;
            case Side.Server:
                ServerLoop();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }
        
        HotReloadHandler.OnUpdateApplication -= RequestHotReload;
        _bootstrap.Shutdown();
        
        Console.WriteLine("[Worker] Exited cleanly");
    }
    
    private static HotReloadState? GetHotReloadState()
    {
        Console.WriteLine("[Worker] Collecting hot reload state...");
        
        try
        {
            var moduleData = _bootstrap.GetHotReloadData();
            
            var state = new HotReloadState
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            foreach (var (moduleName, data) in moduleData)
            {
                state.ModuleStates[moduleName] = data;
                Console.WriteLine($"[Worker] Collected state from module: {moduleName} ({data.Length} bytes)");
            }
            
            Console.WriteLine($"[Worker] Total modules with state: {state.ModuleStates.Count}");
            
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

    private static void ServerLoop()
    {
        
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
            
            // Check if state was collected during Execute() - if so, skip Loop() and exit
            if (_stateCollected)
            {
                break;
            }
            
            _bootstrap.Loop(deltaTime);
            
            lastTime = currentTime;
        }
    }
}
