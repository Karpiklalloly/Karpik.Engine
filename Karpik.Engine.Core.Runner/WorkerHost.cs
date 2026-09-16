using System.Diagnostics;
using Karpik.Engine.Core.Hot;

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
    private readonly Ref<bool> _isRunning = new(true);
    private IpcClient? _ipcClient;
    private Bootstrap _bootstrap = null!;
    private HotReloadState? _initialState;
    private volatile bool _stateCollected;
    private ClientSimulationWorker? _clientSimulationWorker;

    public void Run(string[] args, ConfigureWorkerRuntime configureRuntime) =>
        RunAsync(args, configureRuntime).GetAwaiter().GetResult();

    public void Run(RunnerLaunchArguments launch, ConfigureWorkerRuntime configureRuntime) =>
        RunAsync(launch, configureRuntime).GetAwaiter().GetResult();

    public void Run(
        RunnerLaunchArguments launch,
        ConfigureWorkerRuntime configureRuntime,
        CancellationToken cancellationToken) =>
        RunAsync(launch, configureRuntime, cancellationToken).GetAwaiter().GetResult();

    public Task RunAsync(string[] args, ConfigureWorkerRuntime configureRuntime) =>
        RunAsync(RunnerLaunchArguments.Parse(args), configureRuntime);

    public Task RunAsync(RunnerLaunchArguments launch, ConfigureWorkerRuntime configureRuntime) =>
        RunAsync(launch, configureRuntime, CancellationToken.None);

    public async Task RunAsync(
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
            _ipcClient.OnEditorSnapshotRequest = GetEditorSnapshot;
            _ipcClient.OnShutdownRequest = () =>
            {
                Console.WriteLine("[Worker] Shutdown requested");
                _isRunning.Value = false;
                _stateCollected = true;
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

        WorkerRuntimeConfiguration? runtimeConfiguration = null;
        try
        {
            runtimeConfiguration = await RunEngineAsync(
                launch.Side,
                launch.BundlePath,
                launch.EngineRoot,
                configureRuntime,
                cancellationToken);
        }
        finally
        {
            runtimeConfiguration?.RuntimeLifetime?.Dispose();
            _ipcClient?.Dispose();
            _bootstrap = null!;
        }
    }

    public void RequestHotReload()
    {
        if (_ipcClient == null)
        {
            Console.WriteLine("[Worker] Cannot request hot reload: IPC not connected");
            return;
        }

        Console.WriteLine("[Worker] Requesting hot reload from watcher...");
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

        _bootstrap = new Bootstrap(side, new EngineRunner());
        WorkerRuntimeConfiguration runtimeConfiguration =
            configureRuntime(side, bundleRoot, engineRoot, _bootstrap);

        Dictionary<string, byte[]>? initialHotReloadData = null;
        if (_initialState != null && _initialState.ModuleStates.Count > 0)
        {
            initialHotReloadData = _initialState.ModuleStates;
            Console.WriteLine($"[Worker] Will apply hot reload state from {_initialState.ModuleStates.Count} modules");
        }

        Console.WriteLine(Environment.CurrentManagedThreadId);
        var mainThreadScheduler = _bootstrap.Initialize(Environment.CurrentManagedThreadId, _isRunning, initialHotReloadData);
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

        HotReloadHandler.OnUpdateApplication -= RequestHotReload;
        _bootstrap.ShutdownAsync().GetAwaiter().GetResult();

        Console.WriteLine("[Worker] Exited cleanly");
        return Task.FromResult(runtimeConfiguration);
    }

    private HotReloadState? GetHotReloadState()
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

    private EditorRuntimeSnapshot? GetEditorSnapshot()
    {
        ClientSimulationWorker? simulationWorker = Volatile.Read(ref _clientSimulationWorker);
        return simulationWorker is null
            ? CaptureEditorSnapshotOnCurrentThread()
            : simulationWorker.InvokeAsync(CaptureEditorSnapshotOnCurrentThread).GetAwaiter().GetResult();
    }

    private EditorRuntimeSnapshot? CaptureEditorSnapshotOnCurrentThread() =>
        _bootstrap?.CaptureEditorSnapshot();

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

    private void ServerLoop(MainThreadScheduler mainThreadScheduler, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        double nextTickTime = stopwatch.Elapsed.TotalSeconds;

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
                Console.WriteLine($"Server overloading! Fixed tick backlog preserved. Lag: {currentTime - nextTickTime:F4}s");
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
