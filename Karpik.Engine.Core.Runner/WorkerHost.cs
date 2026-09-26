using System.Diagnostics;
using Karpik.Engine.Core.Hot;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core.Runner;

/// <summary>Runtime resources owned for the whole worker lifetime.</summary>
internal sealed record WorkerRuntimeConfiguration(string ModuleDirectory, IDisposable? RuntimeLifetime);

/// <summary>
/// Registers engine types or a static composition into the worker bootstrap.
/// Returns the module directory reported to the watcher in the ready message
/// and any runtime lifetime that must stay alive until the worker exits.
/// </summary>
internal delegate WorkerRuntimeConfiguration ConfigureWorkerRuntime(
    Side side,
    string bundleRoot,
    string engineRoot,
    Bootstrap bootstrap);

/// <summary>
/// Shared worker process lifecycle: launch arguments, hot-reload state transfer,
/// IPC connection and the fixed-dt main loops. The universal Dynamic runner
/// (<see cref="Program"/>) and game-specific Static hosts
/// (<see cref="StaticEngineHost"/>) both drive this class.
/// </summary>
internal sealed class WorkerHost
{
    private readonly ILoggerFactory _hostLoggerFactory = HostLogging.CreateDefaultFactory();
    private readonly ILogger<WorkerHost> _logger;
    private readonly Ref<bool> _isRunning = new(true);
    private IpcClient? _ipcClient;
    private Bootstrap _bootstrap = null!;
    private HotReloadState? _initialState;
    private volatile bool _stateCollected;
    private ClientSimulationWorker? _clientSimulationWorker;
    private MainThreadScheduler? _mainThreadScheduler;
    private WorkerRuntimeConfiguration? _runtimeConfiguration;

    public WorkerHost()
    {
        _logger = _hostLoggerFactory.CreateLogger<WorkerHost>();
    }

    public void Run(string[] args, ConfigureWorkerRuntime configureRuntime) =>
        RunAsync(args, configureRuntime).GetAwaiter().GetResult();

    public void Run(RunnerLaunchArguments launch, ConfigureWorkerRuntime configureRuntime) =>
        RunAsync(launch, configureRuntime).GetAwaiter().GetResult();

    public void Run(
        RunnerLaunchArguments launch,
        ConfigureWorkerRuntime configureRuntime,
        CancellationToken cancellationToken) =>
        RunAsync(launch, configureRuntime, cancellationToken).GetAwaiter().GetResult();

    public Task RunAsync(string[] args, ConfigureWorkerRuntime configureRuntime)
    {
        RunnerLaunchArguments launch;
        try
        {
            launch = RunnerLaunchArguments.Parse(args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid worker launch");
            _hostLoggerFactory.Dispose();
            throw;
        }
        return RunAsync(launch, configureRuntime);
    }

    public Task RunAsync(RunnerLaunchArguments launch, ConfigureWorkerRuntime configureRuntime) =>
        RunAsync(launch, configureRuntime, CancellationToken.None);

    public async Task RunAsync(
        RunnerLaunchArguments launch,
        ConfigureWorkerRuntime configureRuntime,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker starting");
        try
        {
            await RunCoreAsync(launch, configureRuntime, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Worker engine crashed");
            throw;
        }
        finally
        {
            try
            {
                StopIpc();
            }
            finally
            {
                _hostLoggerFactory.Dispose();
            }
        }
    }

    private async Task RunCoreAsync(
        RunnerLaunchArguments launch,
        ConfigureWorkerRuntime configureRuntime,
        CancellationToken cancellationToken)
    {
        var pipeName = launch.PipeName;
        var stateBase64 = launch.State;
        var stateFile = launch.StateFile;
        var waitForDebugger = launch.WaitForDebugger;

        if (!string.IsNullOrWhiteSpace(pipeName))
        {
            AppContext.SetData("Karpik.HotReload.PipeName", pipeName);
        }

        if (waitForDebugger)
        {
            _logger.LogInformation("Waiting for debugger to attach");
            while (!Debugger.IsAttached)
            {
                Thread.Sleep(100);
            }
            _logger.LogInformation("Debugger attached");
        }

        if (!string.IsNullOrEmpty(stateFile))
        {
            try
            {
                var stateBytes = File.ReadAllBytes(stateFile);
                _initialState = HotReloadState.Deserialize(stateBytes);
                _logger.LogInformation("Loaded initial state with {ModuleCount} modules", _initialState.ModuleStates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize initial state file {StateFile}", stateFile);
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
                _logger.LogInformation("Loaded initial state with {ModuleCount} modules", _initialState.ModuleStates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize initial state");
            }
        }

        if (!string.IsNullOrEmpty(pipeName))
        {
            _ipcClient = new IpcClient(pipeName, _hostLoggerFactory);

            _ipcClient.OnStateRequest = GetHotReloadState;
            _ipcClient.OnEditorSnapshotRequest = GetEditorSnapshot;
            _ipcClient.OnShutdownRequest = () =>
            {
                _logger.LogInformation("Shutdown requested");
                _isRunning.Value = false;
                _stateCollected = true;
            };

            try
            {
                _ipcClient.ConnectAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to watcher; running in standalone mode");
                StopIpc();
            }
        }
        else
        {
            _logger.LogInformation("No pipe name provided; running in standalone mode");
        }

        try
        {
            await RunEngineAsync(
                launch.Side,
                launch.BundlePath,
                launch.EngineRoot,
                configureRuntime,
                cancellationToken);
        }
        finally
        {
            try
            {
                StopIpc();
            }
            finally
            {
                try
                {
                    if (_bootstrap is not null)
                    {
                        _bootstrap.ShutdownAsync().GetAwaiter().GetResult();
                    }
                }
                finally
                {
                    _runtimeConfiguration?.RuntimeLifetime?.Dispose();
                    _runtimeConfiguration = null;
                    _bootstrap = null!;
                    _mainThreadScheduler = null;
                }
            }
        }
        _logger.LogInformation("Worker exited cleanly");
    }

    private void StopIpc()
    {
        IpcClient? client = _ipcClient;
        if (client is null)
        {
            return;
        }

        Task stop = client.StopAsync();
        Exception? schedulerError = null;
        while (!stop.IsCompleted && _mainThreadScheduler is { } scheduler)
        {
            try
            {
                scheduler.Execute();
            }
            catch (Exception ex)
            {
                schedulerError ??= ex;
            }
            Thread.Yield();
        }
        stop.GetAwaiter().GetResult();
        _ipcClient = null;
        if (schedulerError is not null)
        {
            throw schedulerError;
        }
    }

    public void RequestHotReload()
    {
        if (_ipcClient == null)
        {
            _logger.LogWarning("Cannot request hot reload: IPC is not connected");
            return;
        }

        _logger.LogInformation("Requesting hot reload from watcher");
        _ = _ipcClient.RequestHotReloadAsync();
    }

    private Task<WorkerRuntimeConfiguration> RunEngineAsync(
        Side side,
        string bundleRoot,
        string engineRoot,
        ConfigureWorkerRuntime configureRuntime,
        CancellationToken cancellationToken)
    {
        HotReloadHandler.OnUpdateApplication += RequestHotReload;
        try
        {
            return RunEngineCoreAsync(side, bundleRoot, engineRoot, configureRuntime, cancellationToken);
        }
        finally
        {
            HotReloadHandler.OnUpdateApplication -= RequestHotReload;
        }
    }

    private Task<WorkerRuntimeConfiguration> RunEngineCoreAsync(
        Side side,
        string bundleRoot,
        string engineRoot,
        ConfigureWorkerRuntime configureRuntime,
        CancellationToken cancellationToken)
    {
        _bootstrap = new Bootstrap(side, new EngineRunner(_hostLoggerFactory), _hostLoggerFactory);
        WorkerRuntimeConfiguration runtimeConfiguration =
            configureRuntime(side, bundleRoot, engineRoot, _bootstrap);
        _runtimeConfiguration = runtimeConfiguration;

        Dictionary<string, byte[]>? initialHotReloadData = null;
        if (_initialState != null && _initialState.ModuleStates.Count > 0)
        {
            initialHotReloadData = _initialState.ModuleStates;
            _logger.LogInformation("Will apply hot reload state from {ModuleCount} modules", _initialState.ModuleStates.Count);
        }

        _logger.LogDebug("Worker main thread: {ThreadId}", Environment.CurrentManagedThreadId);
        var mainThreadScheduler = _bootstrap.Initialize(Environment.CurrentManagedThreadId, _isRunning, initialHotReloadData);
        _mainThreadScheduler = mainThreadScheduler;
        mainThreadScheduler.Execute();
        // The game loop must remain on the thread that owns this scheduler.
        // Awaiting in a console host may resume on a thread-pool thread instead.
        _bootstrap.Startup.GetAwaiter().GetResult();

        _ipcClient?.SetScheduler(mainThreadScheduler);

        switch (side)
        {
            case Side.Client:
                using (var simulationWorker = new ClientSimulationWorker(
                           _bootstrap,
                           _bootstrap.ClientFrameMetrics))
                {
                    Volatile.Write(ref _clientSimulationWorker, simulationWorker);
                    try
                    {
                        _ipcClient?.SendReadyAsync(runtimeConfiguration.ModuleDirectory).Wait(cancellationToken);
                        ClientLoop(mainThreadScheduler, simulationWorker, cancellationToken);
                    }
                    finally
                    {
                        Volatile.Write(ref _clientSimulationWorker, null);
                    }
                }
                break;
            case Side.Server:
                _ipcClient?.SendReadyAsync(runtimeConfiguration.ModuleDirectory).Wait(cancellationToken);
                ServerLoop(mainThreadScheduler, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }

        return Task.FromResult(runtimeConfiguration);
    }

    private HotReloadState? GetHotReloadState()
    {
        _logger.LogInformation("Collecting hot reload state");

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
                _logger.LogInformation("Collected state from module {ModuleName} ({ByteCount} bytes)", moduleName, data.Length);
            }

            _logger.LogInformation("Total modules with state: {ModuleCount}", state.ModuleStates.Count);

            _stateCollected = true;
            _isRunning.Value = false;

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect hot reload state");
            return null;
        }
    }

    private EditorRuntimeSnapshot? GetEditorSnapshot()
    {
        ClientSimulationWorker? simulationWorker = Volatile.Read(ref _clientSimulationWorker);
        return simulationWorker is null
            ? CaptureEditorSnapshotOnCurrentThread()
            : simulationWorker.InvokeAsync(CaptureEditorSnapshotOnCurrentThread).GetAwaiter().GetResult();
    }

    private EditorRuntimeSnapshot? CaptureEditorSnapshotOnCurrentThread() =>
        _bootstrap?.CaptureEditorSnapshot();

    private void TryDeleteStateFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete state file {StateFile}", path);
        }
    }

    private void ServerLoop(MainThreadScheduler mainThreadScheduler, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        double nextTickTime = stopwatch.Elapsed.TotalSeconds;
        bool overloaded = false;

        while (_isRunning.Value && !cancellationToken.IsCancellationRequested)
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
                if (!overloaded)
                {
                    _logger.LogWarning("Server fixed ticks overloaded; backlog preserved. Lag: {LagSeconds:F4}s", currentTime - nextTickTime);
                    overloaded = true;
                }
            }
            else
            {
                overloaded = false;
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

    private void ClientLoop(
        MainThreadScheduler mainThreadScheduler,
        ClientSimulationWorker simulationWorker,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        double lastSimulationRequestTime = 0;
        var metrics = _bootstrap.ClientFrameMetrics;

        while (_isRunning.Value && !cancellationToken.IsCancellationRequested)
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
