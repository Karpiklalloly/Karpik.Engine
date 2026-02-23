namespace Karpik.Engine.Core;

internal class Bootstrap
{
    private ServiceProvider _serviceProvider = null!;
    private MainThreadScheduler _mainThreadScheduler = null!;
    private Ref<bool> _isRunning = null!;
    private Application _application = new();
    private EngineRunner _runner = new();
    
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
        _runner.RegisterTypes(types);
    }

    private void Setup(Dictionary<string, byte[]>? hotReloadData)
    {
        Job.Initialize(new Jobs.JobSystem());
        
        _serviceProvider = new ServiceProvider();
        _serviceProvider.Register(_mainThreadScheduler);
        _serviceProvider.Register(_application);
        
        _runner.Setup(_serviceProvider, _mainThreadScheduler, hotReloadData ?? new Dictionary<string, byte[]>());
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
