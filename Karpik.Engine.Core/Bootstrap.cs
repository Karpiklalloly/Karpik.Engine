using System.Runtime.Loader;

namespace Karpik.Engine.Core;

internal class Bootstrap : IClientSimulationLoop
{
    private MainThreadScheduler _mainThreadScheduler = null!;
    private Ref<bool> _isRunning = null!;
    private Application _application;
    private readonly ClientFrameMetrics _clientFrameMetrics = new();
    private IEngineRunner _runner = null!;
    [Obsolete("Legacy in-process compatibility only. Runtime hosts must inject an IEngineRunner.")]
    public Bootstrap(Side side)
    {
        _application = new Application(side);
    }

    public Bootstrap(Side side, IEngineRunner runner)
    {
        _application = new Application(side);
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
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
            var type = types.First(x => !x.IsAbstract && typeof(IEngineRunner).IsAssignableFrom(x));
            _runner = (IEngineRunner)Activator.CreateInstance(type)!;
        }
        _runner.RegisterTypes(types);
    }

    private void Setup(Dictionary<string, byte[]>? hotReloadData)
    {
        Job.Initialize(new Jobs.JobSystem());
        
        _runner.Setup(_application, _mainThreadScheduler, _clientFrameMetrics, hotReloadData ?? new Dictionary<string, byte[]>());
    }
    
    public void Loop(double dt)
    {
        _runner.Run(dt);
        _isRunning.Value = _application.IsRunning;
    }

    public void RunMainThreadBegin()
    {
        _runner.RunMainThreadBegin();
    }

    public void RunMainThreadFrameBegin()
    {
        _runner.RunMainThreadFrameBegin();
    }

    public void RunGameplayFrame(double dt)
    {
        _runner.RunGameplayFrame(dt);
    }

    public void RunRender()
    {
        _runner.RunRender();
    }

    public bool IsApplicationRunning => _runner.IsApplicationRunning;
    public ClientFrameMetrics ClientFrameMetrics => _clientFrameMetrics;
    
    public void Shutdown()
    {
        _runner.Destroy();
    }
    
    public Dictionary<string, byte[]> GetHotReloadData()
    {
        return _runner.GetHotReloadData();
    }

    public EditorRuntimeSnapshot CaptureEditorSnapshot()
    {
        return _runner.CaptureEditorSnapshot();
    }
}
