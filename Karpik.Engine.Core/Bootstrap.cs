using System.Runtime.Loader;

using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core;

internal class Bootstrap : IClientSimulationLoop
{
    private MainThreadScheduler _mainThreadScheduler = null!;
    private Ref<bool> _isRunning = null!;
    private Application _application;
    private readonly ClientFrameMetrics _clientFrameMetrics = new();
    private IEngineRunner _runner = null!;
    private Task _startup = Task.CompletedTask;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILoggerFactory? _ownedLoggerFactory;
    private readonly ILogger<Bootstrap> _logger;

    /// <summary>Runner-assembly seam for static composition registration.</summary>
    internal IEngineRunner Runner => _runner;
    public Task Startup => _startup;
    [Obsolete("Legacy in-process compatibility only. Runtime hosts must inject an IEngineRunner.")]
    public Bootstrap(Side side)
        : this(side, HostLogging.CreateDefaultFactory(), ownsFactory: true)
    {
    }

    public Bootstrap(Side side, ILoggerFactory loggerFactory)
        : this(side, loggerFactory, ownsFactory: false)
    {
    }

    private Bootstrap(Side side, ILoggerFactory loggerFactory, bool ownsFactory)
    {
        _application = new Application(side);
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _ownedLoggerFactory = ownsFactory ? loggerFactory : null;
        _logger = loggerFactory.CreateLogger<Bootstrap>();
    }

    public Bootstrap(Side side, IEngineRunner runner)
        : this(side, runner, HostLogging.CreateDefaultFactory(), ownsFactory: true)
    {
    }

    public Bootstrap(Side side, IEngineRunner runner, ILoggerFactory loggerFactory)
        : this(side, runner, loggerFactory, ownsFactory: false)
    {
    }

    private Bootstrap(Side side, IEngineRunner runner, ILoggerFactory loggerFactory, bool ownsFactory)
        : this(side, loggerFactory, ownsFactory)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }
        
    public MainThreadScheduler Initialize(int mainThreadId, Ref<bool> isRunning, Dictionary<string, byte[]>? initialHotReloadState = null)
    {
        _mainThreadScheduler = new MainThreadScheduler(mainThreadId, _loggerFactory);
        _isRunning = isRunning;
        
        if (initialHotReloadState != null && initialHotReloadState.Count > 0)
        {
            _logger.LogInformation("Starting with state from previous worker (process isolation)");
        }
        
        _startup = SetupAsync(initialHotReloadState);
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

    private Task SetupAsync(Dictionary<string, byte[]>? hotReloadData)
    {
        Job.Initialize(new Jobs.JobSystem());

        return _runner.SetupAsync(
            _application,
            _mainThreadScheduler,
            _clientFrameMetrics,
            hotReloadData ?? new Dictionary<string, byte[]>());
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
        try
        {
            _runner.Destroy();
        }
        finally
        {
            _ownedLoggerFactory?.Dispose();
        }
    }

    public async Task ShutdownAsync()
    {
        try
        {
            await _runner.DestroyAsync();
        }
        finally
        {
            _ownedLoggerFactory?.Dispose();
        }
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
