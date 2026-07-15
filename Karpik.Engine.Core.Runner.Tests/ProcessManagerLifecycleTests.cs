using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner.TestWorker;
using Xunit;

public sealed class ProcessManagerLifecycleTests
{
    [Fact]
    public async Task WorkerRequestedReload_RacingStop_DoesNotStartReplacement()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager();
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        runtime.RequestReload();
        await runtime.WaitForAsync("state-response-sent");

        await manager.StopWorkerAsync();
        await WaitUntilAsync(() => !manager.IsReloadInProgress);

        Assert.False(manager.IsWorkerRunning);
        Assert.Equal(1, runtime.StartCount);
    }

    [Fact]
    public async Task WorkerRequestedReload_RacingDispose_DoesNotStartReplacement()
    {
        using var runtime = new LifecycleRuntime();
        var manager = runtime.CreateManager();
        int exits = 0;
        manager.OnWorkerExited += _ => Interlocked.Increment(ref exits);
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        runtime.SlowNextBundleValidation();
        runtime.RequestReload();
        await runtime.WaitForAsync("state-response-sent");

        await WaitUntilAsync(() => manager.IsReloadInProgress && manager.WorkerProcessId < 0);
        await Task.Run(manager.Dispose);
        await WaitUntilAsync(() => !manager.IsReloadInProgress);
        await Task.Delay(1_000);

        Assert.False(manager.IsWorkerRunning);
        Assert.Equal(1, runtime.StartCount);
        Assert.Equal(0, Volatile.Read(ref exits));
    }

    [Fact]
    public async Task DirectReloadAndStop_RunningConcurrently_StopWins()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager();
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));

        Task reload = manager.HotReloadAsync();
        await runtime.WaitForAsync("state-requested");
        runtime.ReleaseState();
        await runtime.WaitForAsync("state-response-sent");
        Task stop = manager.StopWorkerAsync();
        await Task.WhenAll(reload, stop);

        Assert.False(manager.IsWorkerRunning);
        Assert.Equal(1, runtime.StartCount);
    }

    [Fact]
    public async Task HotReloadCallback_CanDisposeWithoutReenteringTransitionGate()
    {
        using var runtime = new LifecycleRuntime();
        var manager = runtime.CreateManager();
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        manager.OnHotReloadRequested += _ => manager.Dispose();

        Task reload = manager.HotReloadAsync();
        await runtime.WaitForAsync("state-requested");
        runtime.ReleaseState();
        await reload.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(manager.IsWorkerRunning);
    }

    [Fact]
    public async Task StartQueuedBeforeStop_DoesNotClearConcurrentStopIntent()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager();
        var transitionGate = (SemaphoreSlim)typeof(ProcessManager).GetField(
            "_transitionGate",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        await transitionGate.WaitAsync();
        try
        {
            Task start = manager.StartWorkerAsync();
            await Task.Delay(20);
            Task stop = manager.StopWorkerAsync();
            transitionGate.Release();
            await Task.WhenAll(start, stop);
        }
        catch
        {
            if (transitionGate.CurrentCount == 0)
            {
                transitionGate.Release();
            }
            throw;
        }

        Assert.False(manager.IsWorkerRunning);
        Assert.Equal(0, runtime.StartCount);
    }

    [Fact]
    public async Task SuccessfulReload_DoesNotPublishPlannedOldWorkerExitAfterReplacementReady()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager();
        int exits = 0;
        int ready = 0;
        manager.OnWorkerExited += _ => Interlocked.Increment(ref exits);
        manager.OnWorkerReady += () => Interlocked.Increment(ref ready);
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        await WaitUntilAsync(() => Volatile.Read(ref ready) == 1);

        Task reload = manager.HotReloadAsync();
        await runtime.WaitForAsync("state-requested");
        runtime.ReleaseState();
        await reload;
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        await WaitUntilAsync(() => Volatile.Read(ref ready) == 2);
        await Task.Delay(100);

        Assert.Equal(0, Volatile.Read(ref exits));
    }

    [Fact]
    public void ReloadGate_AllowsExactlyOneConcurrentOwner()
    {
        var manager = (ProcessManager)RuntimeHelpers.GetUninitializedObject(typeof(ProcessManager));
        MethodInfo? tryBegin = typeof(ProcessManager).GetMethod(
            "TryBeginReload",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo? end = typeof(ProcessManager).GetMethod(
            "EndReload",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(tryBegin);
        Assert.NotNull(end);
        int owners = 0;
        Parallel.For(0, 256, _ =>
        {
            if ((bool)tryBegin.Invoke(manager, null)!)
            {
                Interlocked.Increment(ref owners);
            }
        });

        Assert.Equal(1, owners);
        end.Invoke(manager, null);
        Assert.False(manager.IsReloadInProgress);
    }

    [Fact]
    public async Task WaitForExitAsync_TimeoutRemovesHandlerFromCapturedProcess()
    {
        var manager = (ProcessManager)RuntimeHelpers.GetUninitializedObject(typeof(ProcessManager));
        using Process current = Process.GetProcessById(Environment.ProcessId);
        current.EnableRaisingEvents = true;
        FieldInfo worker = typeof(ProcessManager).GetField(
            "_workerProcess",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        worker.SetValue(manager, current);
        int baseline = ExitedSubscriberCount(current);

        Assert.False(await manager.WaitForExitAsync(TimeSpan.FromMilliseconds(20)));

        Assert.Equal(baseline, ExitedSubscriberCount(current));
        worker.SetValue(manager, null);
    }

    private static int ExitedSubscriberCount(Process process)
    {
        FieldInfo field = typeof(Process).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
                typeof(MulticastDelegate).IsAssignableFrom(candidate.FieldType)
                && candidate.Name.Contains("Exited", StringComparison.OrdinalIgnoreCase));
        return ((MulticastDelegate?)field.GetValue(process))?.GetInvocationList().Length ?? 0;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class LifecycleRuntime : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "KarpikProcessLifecycleTests",
            Guid.NewGuid().ToString("N"));
        private readonly string _controlPath;

        public string BundlePath { get; }
        public string RunnerPath { get; }

        public int StartCount => File.Exists(Path.Combine(_controlPath, "starts.log"))
            ? File.ReadAllLines(Path.Combine(_controlPath, "starts.log")).Length
            : 0;

        public LifecycleRuntime()
        {
            string workerAssembly = typeof(WorkerMarker).Assembly.Location;
            RunnerPath = OperatingSystem.IsWindows()
                ? Path.ChangeExtension(workerAssembly, ".exe")
                : Path.Combine(Path.GetDirectoryName(workerAssembly)!, Path.GetFileNameWithoutExtension(workerAssembly));
            BundlePath = Path.Combine(_root, "bundle");
            _controlPath = Path.Combine(BundlePath, "reload", "state", "lifecycle-tests");
            Directory.CreateDirectory(Path.Combine(BundlePath, "Content"));
            File.WriteAllText(Path.Combine(BundlePath, "Content", "content.txt"), "content");
            string modules = Path.Combine(BundlePath, "modules.version.1");
            Directory.CreateDirectory(modules);
            File.WriteAllText(Path.Combine(BundlePath, "runtime-bundle.side"), "karpik-runtime-side-v1:Server\n");
            File.WriteAllText(Path.Combine(BundlePath, ".complete"), RuntimeBundleLayout.BundleCompletionMarker);
            File.WriteAllText(Path.Combine(modules, ".complete"), RuntimeBundleLayout.ModuleCompletionMarker);
            File.WriteAllText(Path.Combine(modules, "modules.list"), "Game.dll\n");
            File.WriteAllText(Path.Combine(modules, "Game.dll"), "game");
        }

        public ProcessManager CreateManager()
        {
            var launch = new RuntimeLaunchOptions(Side.Server, RunnerPath, BundlePath);
            return new ProcessManager(
                launch,
                new HotReloadOptions
                {
                    Mode = HotReloadMode.RestartWorker,
                    WorkerConnectionTimeout = TimeSpan.FromSeconds(10),
                    StateRequestTimeout = TimeSpan.FromSeconds(10),
                    GracefulShutdownTimeout = TimeSpan.FromMilliseconds(250)
                });
        }

        public void RequestReload()
        {
            Directory.CreateDirectory(_controlPath);
            File.WriteAllText(Path.Combine(_controlPath, "request-reload"), "reload");
            ReleaseState();
        }

        public void ReleaseState()
        {
            Directory.CreateDirectory(_controlPath);
            File.WriteAllText(Path.Combine(_controlPath, "release-state"), "release");
        }

        public void SlowNextBundleValidation()
        {
            string padding = Path.Combine(BundlePath, "Content", "validation-padding");
            Directory.CreateDirectory(padding);
            for (int index = 0; index < 4_000; index++)
            {
                File.WriteAllText(Path.Combine(padding, $"{index:D5}.txt"), string.Empty);
            }
        }

        public async Task WaitForAsync(string name)
        {
            string path = Path.Combine(_controlPath, name);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(path))
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        public void Dispose()
        {
            string starts = Path.Combine(_controlPath, "starts.log");
            if (File.Exists(starts))
            {
                foreach (string line in File.ReadAllLines(starts))
                {
                    if (!int.TryParse(line, out int processId))
                    {
                        continue;
                    }
                    try
                    {
                        using Process process = Process.GetProcessById(processId);
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(2_000);
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
