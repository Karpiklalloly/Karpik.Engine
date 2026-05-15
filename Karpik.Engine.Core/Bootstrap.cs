using System.Runtime.Loader;

namespace Karpik.Engine.Core;

internal class Bootstrap
{
    private MainThreadScheduler _mainThreadScheduler = null!;
    private Ref<bool> _isRunning = null!;
    private Application _application;
    private IEngineRunner _runner = null!;
    private AssemblyLoadContext _context;

    public Bootstrap(Side side)
    {
        _application = new Application(side);

        _context = new AssemblyLoadContext("CORE");
        string s = Directory.GetCurrentDirectory();
        _context.LoadFromAssemblyPath(Path.Combine(s, "Karpik.Engine.Core.Runner.dll"));
        var type = _context.Assemblies
            .SelectMany(x => x.GetTypes())
            .First(x => x.IsAssignableTo(typeof(IEngineRunner))
            || x.IsAssignableFrom(typeof(IEngineRunner)));
        _runner = (IEngineRunner)Activator.CreateInstance(type)!;
    }
        
    public MainThreadScheduler Initialize(int mainThreadId, Ref<bool> isRunning, Dictionary<string, byte[]>? initialHotReloadState = null)
    {
        _mainThreadScheduler = new MainThreadScheduler(mainThreadId);
        _isRunning = isRunning;
        
        if (initialHotReloadState != null && initialHotReloadState.Count > 0)
        {
            Console.WriteLine("[Bootstrap] Starting with state from previous worker (process isolation)");
        }
        
        _mainThreadScheduler.Schedule(() => Setup(initialHotReloadState));
        return _mainThreadScheduler;
    }

    public void RegisterTypes(Type[] types)
    {
        if (_runner is null)
        {
            var type = types.First(x => x.IsAssignableTo(typeof(IEngineRunner))
                                       || x.IsAssignableFrom(typeof(IEngineRunner)));
            _runner = (IEngineRunner)Activator.CreateInstance(type)!;
        }
        _runner.RegisterTypes(types);
    }

    private void Setup(Dictionary<string, byte[]>? hotReloadData)
    {
        Job.Initialize(new Jobs.JobSystem());
        
        _runner.Setup(_application, _mainThreadScheduler, hotReloadData ?? new Dictionary<string, byte[]>());
    }
    
    public void Loop(double dt)
    {
        _runner.Run(dt);
        _isRunning.Value = _application.IsRunning;
    }
    
    public void Shutdown()
    {
        _runner.Destroy();
    }
    
    public Dictionary<string, byte[]> GetHotReloadData()
    {
        return _runner.GetHotReloadData();
    }
}
