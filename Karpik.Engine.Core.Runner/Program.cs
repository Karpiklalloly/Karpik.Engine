using System.Diagnostics;
using Karpik.Engine.Core.Hot;

namespace Karpik.Engine.Core.Runner;

public class Program
{
    private static IpcClient? _ipcClient;
    private static Bootstrap _bootstrap;
    private static Ref<bool> _isRunning = new(true);
    private static HotReloadState? _initialState;
    private static volatile bool _stateCollected = false;
    
    public static void Main(string[] args)
    {
        Console.WriteLine("[Worker] Starting...");
        
        var pipeName = ParseArg(args, "--pipe-name");
        var stateBase64 = ParseArg(args, "--state");
        var stateFile = ParseArg(args, "--state-file");
        var waitForDebugger = HasArg(args, "--wait-for-debugger");
        var side = ParseArg(args, "--side");

        if (!string.IsNullOrWhiteSpace(pipeName))
        {
            AppContext.SetData("Karpik.HotReload.PipeName", pipeName);
        }
        
        Enum.TryParse(side, out Side s);
        
        if (waitForDebugger)
        {
            Console.WriteLine("[Worker] Waiting for debugger to attach...");
            while (!Debugger.IsAttached)
            {
                Thread.Sleep(100);
            }
            Console.WriteLine("[Worker] Debugger attached!");
        }
        
        if (!string.IsNullOrEmpty(stateFile))
        {
            try
            {
                var stateBytes = File.ReadAllBytes(stateFile);
                _initialState = HotReloadState.Deserialize(stateBytes);
                Console.WriteLine($"[Worker] Loaded initial state with {_initialState.ModuleStates.Count} modules");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Worker] Failed to deserialize initial state file '{stateFile}': {ex.Message}");
            }
            finally
            {
                TryDeleteStateFile(stateFile);
            }
        }
        else if (!string.IsNullOrEmpty(stateBase64))
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
                ServerLoop(mainThreadScheduler);
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

    private static void TryDeleteStateFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Worker] Failed to delete state file '{path}': {ex.Message}");
        }
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
                if (_stateCollected)
                {
                    break;
                }

                _bootstrap.Loop(Application.TICK_DT);
                nextTickTime += Application.TICK_DT;
                loops++;
            }

            if (_stateCollected)
            {
                break;
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
