using System.Reflection;
using Karpik.Engine.Core.ModuleManagement;

namespace Karpik.Engine.Core;

/// <summary>
/// Bootstrap class for engine initialization.
/// Simplified for Process Isolation architecture - hot reload is handled by restarting the worker process.
/// </summary>
public class Bootstrap
{
    private ServiceProvider _serviceProvider = null!;
    private MainThreadScheduler _mainThreadScheduler = null!;
    private Ref<bool> _isRunning = null!;
    private Application _application = new();
    private IRunner _runner = null!;

    /// <summary>
    /// Initialize the engine with optional hot reload state from previous worker process.
    /// </summary>
    /// <param name="mainThreadId">The main thread ID for scheduler</param>
    /// <param name="isRunning">Reference to running state flag</param>
    /// <param name="initialHotReloadState">Optional state from previous worker process (for process isolation)</param>
    /// <returns>The main thread scheduler</returns>
    public MainThreadScheduler Initialize(int mainThreadId, Ref<bool> isRunning, Dictionary<string, byte[]>? initialHotReloadState = null)
    {
        _mainThreadScheduler = new MainThreadScheduler(mainThreadId);
        _isRunning = isRunning;
        
        // Process isolation: this is a FRESH START with initial state, NOT a hot reload
        // hotReload=true is only for in-process module replacement (which we don't use anymore)
        bool isHotReload = false;
        
        if (initialHotReloadState != null && initialHotReloadState.Count > 0)
        {
            Console.WriteLine("[Bootstrap] Starting with state from previous worker (process isolation)");
        }
        
        _mainThreadScheduler.Schedule(() => Setup(isHotReload, initialHotReloadState));
        return _mainThreadScheduler;
    }

    /// <summary>
    /// Register types from loaded assemblies. Finds EngineRunner and registers modules.
    /// </summary>
    public void RegisterTypes(Type[] types)
    {
        if (_runner is null)
        {
            var filter1 = types.Where(t => t.IsClass && !t.IsAbstract);
            var filter2 = filter1.Where(x => x.FullName != null && x.FullName.Contains("EngineRunner"));
            var type = filter2.FirstOrDefault(t =>
            {
                var a = typeof(IRunner).IsAssignableFrom(t);
                var b = typeof(IRunner).IsAssignableTo(t);
                return a || b;
            });
            if (type is null)
            {
                throw new Exception("EngineRunner type not found in loaded assemblies");
            }
            _runner = (IRunner)Activator.CreateInstance(type)!;
        }
        _runner.RegisterTypes(types);
    }

    private void Setup(bool hotReload, Dictionary<string, byte[]>? hotReloadData)
    {
        Job.Initialize(new Jobs.JobSystem());
        
        _serviceProvider = new ServiceProvider();
        _serviceProvider.Register(_mainThreadScheduler);
        _serviceProvider.Register(_application);
        
        _runner.Setup(_serviceProvider, hotReload, _mainThreadScheduler, null, hotReloadData ?? new Dictionary<string, byte[]>());
    }

    /// <summary>
    /// Run one iteration of the engine loop.
    /// </summary>
    public void Loop(double dt)
    {
        _runner.Run(dt);
        _isRunning.Value = _application.IsRunning;
    }

    /// <summary>
    /// Shutdown the engine gracefully.
    /// </summary>
    public void Shutdown()
    {
        _runner.Destroy();
    }
    
    /// <summary>
    /// Collects hot reload state from all modules implementing IModuleHotReload.
    /// Used by the process isolation system to transfer state between worker processes.
    /// </summary>
    public Dictionary<string, byte[]> GetHotReloadData()
    {
        if (_runner == null)
        {
            Console.WriteLine("[Bootstrap] Cannot get hot reload data: runner not initialized");
            return new Dictionary<string, byte[]>();
        }
        
        return _runner.GetHotReloadData();
    }
    
    /// <summary>
    /// Gets the current list of loaded modules.
    /// </summary>
    public List<IModule> GetModules()
    {
        return _runner?.GetModules() ?? new List<IModule>();
    }
}
