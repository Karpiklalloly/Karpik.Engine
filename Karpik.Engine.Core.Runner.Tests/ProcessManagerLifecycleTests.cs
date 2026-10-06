using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner.TestWorker;
using Xunit;

public sealed class ProcessManagerLifecycleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task WorkerRequestedReload_RacingStop_DoesNotStartReplacement(int signalWriteDelayMilliseconds)
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager();
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        runtime.PauseNextStateResponse();
        runtime.RequestReload(signalWriteDelayMilliseconds);
        await runtime.WaitForAsync("state-response-ready");

        Task stop = manager.StopWorkerAsync();
        runtime.ReleaseStateResponse();
        await stop;
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
        Task stop = manager.StopWorkerAsync();
        runtime.ReleaseState();
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
        using var manager = runtime.CreateManager(captureWorkerOutput: true);
        int exits = 0;
        int ready = 0;
        manager.OnWorkerExited += _ => Interlocked.Increment(ref exits);
        manager.OnWorkerReady += () => Interlocked.Increment(ref ready);
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        await WaitUntilAsync(() => Volatile.Read(ref ready) == 1);
        Process oldProcess = GetPrivateField<Process>(manager, "_workerProcess");
        Task oldMonitor = GetPrivateField<Task>(manager, "_monitorTask");

        Task reload = manager.HotReloadAsync();
        await runtime.WaitForAsync("state-requested");
        runtime.ReleaseState();
        await reload;
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        await WaitUntilAsync(() => Volatile.Read(ref ready) == 2);
        await Task.Delay(100);

        Assert.Equal(0, Volatile.Read(ref exits));
        Assert.Equal(0, ProcessSubscriberCount(oldProcess, "Exited"));
        Assert.Equal(0, ProcessSubscriberCount(oldProcess, "OutputDataReceived"));
        Assert.Equal(0, ProcessSubscriberCount(oldProcess, "ErrorDataReceived"));
        Assert.True(IsProcessHandleClosed(oldProcess));
        await oldMonitor.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DelayedOldWorkerExitCallback_DoesNotCancelReplacementReadiness()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager();
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));

        Process oldProcess = GetPrivateField<Process>(manager, "_workerProcess");
        EventHandler oldExitHandler = GetExitHandler(oldProcess);
        oldProcess.Exited -= oldExitHandler;
        oldProcess.Kill(entireProcessTree: true);
        Assert.True(await ProcessManager.WaitForExitAsync(oldProcess, TimeSpan.FromSeconds(10)));

        IpcServer oldIpcServer = GetPrivateField<IpcServer>(manager, "_ipcServer");
        oldIpcServer.Dispose();
        SetPrivateField<object?>(manager, "_workerProcess", null);
        SetPrivateField<object?>(manager, "_ipcServer", null);
        SetPrivateField<object?>(manager, "_workerExitNotification", null);
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));

        oldExitHandler(oldProcess, EventArgs.Empty);
        oldProcess.Dispose();

        Assert.True(manager.IsWorkerReady);
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task DelayedOldWorkerReadyMessage_DoesNotMarkReplacementReadiness()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager();
        int readyNotifications = 0;
        manager.OnWorkerReady += () => Interlocked.Increment(ref readyNotifications);
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        await WaitUntilAsync(() => Volatile.Read(ref readyNotifications) == 1);

        IpcServer oldIpcServer = GetPrivateField<IpcServer>(manager, "_ipcServer");
        Action<IpcMessage> oldMessageHandler = GetMessageHandler(oldIpcServer);
        object replacementReadiness = CreateWorkerReadiness();
        object oldGeneration = GetPrivateField<object>(manager, "_workerGeneration");
        object oldReadiness = GetPrivateField<object>(manager, "_workerReadiness");
        try
        {
            SetPrivateField<object?>(manager, "_workerGeneration", null);
            SetPrivateField(manager, "_workerReadiness", replacementReadiness);

            oldMessageHandler(
                new IpcMessage(
                    IpcMessageType.WorkerReady,
                    System.Text.Encoding.UTF8.GetBytes(
                        Path.Combine(runtime.BundlePath, "modules.version.1"))));
            await Task.Delay(100);

            Assert.False(manager.IsWorkerReady);
            Assert.Equal(1, Volatile.Read(ref readyNotifications));
        }
        finally
        {
            SetPrivateField(manager, "_workerGeneration", oldGeneration);
            SetPrivateField(manager, "_workerReadiness", oldReadiness);
        }
    }

    [Fact]
    public async Task CrashThenPublicRestart_ReleasesOldOwnershipAndDropsDelayedCallbacks()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager(captureWorkerOutput: true);
        int exits = 0;
        int output = 0;
        manager.OnWorkerExited += _ => Interlocked.Increment(ref exits);
        manager.OnWorkerOutput += _ => Interlocked.Increment(ref output);
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));

        Process oldProcess = GetPrivateField<Process>(manager, "_workerProcess");
        EventHandler oldExitHandler = GetProcessHandler<EventHandler>(oldProcess, "Exited");
        DataReceivedEventHandler oldOutputHandler =
            GetProcessHandler<DataReceivedEventHandler>(oldProcess, "OutputDataReceived");
        DataReceivedEventHandler oldErrorHandler =
            GetProcessHandler<DataReceivedEventHandler>(oldProcess, "ErrorDataReceived");
        oldProcess.Exited -= oldExitHandler;
        oldProcess.Kill(entireProcessTree: true);
        Assert.True(await ProcessManager.WaitForExitAsync(oldProcess, TimeSpan.FromSeconds(10)));

        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(0, ProcessSubscriberCount(oldProcess, "Exited"));
        Assert.Equal(0, ProcessSubscriberCount(oldProcess, "OutputDataReceived"));
        Assert.Equal(0, ProcessSubscriberCount(oldProcess, "ErrorDataReceived"));
        Assert.True(IsProcessHandleClosed(oldProcess));

        oldExitHandler(oldProcess, EventArgs.Empty);
        oldOutputHandler(oldProcess, CreateDataReceivedEventArgs("late stdout"));
        oldErrorHandler(oldProcess, CreateDataReceivedEventArgs("late stderr"));
        await Task.Delay(200);

        Assert.Equal(0, Volatile.Read(ref exits));
        Assert.Equal(0, Volatile.Read(ref output));
    }

    [Fact]
    public async Task ReservedOldOutputCallback_CannotCommitAfterGenerationDeactivated()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager(captureWorkerOutput: true);
        using var callbackReserved = new ManualResetEventSlim();
        using var resumeCallback = new ManualResetEventSlim();
        var callbackResumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int output = 0;
        manager.OnWorkerOutput += _ => Interlocked.Increment(ref output);
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));

        Process oldProcess = GetPrivateField<Process>(manager, "_workerProcess");
        EventHandler oldExitHandler = GetProcessHandler<EventHandler>(oldProcess, "Exited");
        DataReceivedEventHandler oldOutputHandler =
            GetProcessHandler<DataReceivedEventHandler>(oldProcess, "OutputDataReceived");
        manager.SetLifecycleCallbackCommitHook(
            () =>
            {
                callbackReserved.Set();
                callbackResumed.TrySetResult(resumeCallback.Wait(TimeSpan.FromSeconds(10)));
            });

        try
        {
            oldOutputHandler(oldProcess, CreateDataReceivedEventArgs("reserved old output"));
            Assert.True(callbackReserved.Wait(TimeSpan.FromSeconds(10)));
            manager.SetLifecycleCallbackCommitHook(null);

            oldProcess.Exited -= oldExitHandler;
            oldProcess.Kill(entireProcessTree: true);
            Assert.True(await ProcessManager.WaitForExitAsync(
                oldProcess,
                TimeSpan.FromSeconds(10)));
            Task replacement = manager.StartWorkerAsync();
            await WaitUntilAsync(() => GetPrivateField<object?>(manager, "_workerGeneration") is null);
            Assert.False(replacement.IsCompleted);
            resumeCallback.Set();
            await replacement;
            Assert.True(await callbackResumed.Task);
            Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));

            Assert.Equal(0, Volatile.Read(ref output));
            Assert.True(manager.IsWorkerReady);
        }
        finally
        {
            manager.SetLifecycleCallbackCommitHook(null);
            resumeCallback.Set();
        }
    }

    [Fact]
    public async Task WorkerOutputCallback_CanDisposeWithoutGenerationDrainDeadlock()
    {
        using var runtime = new LifecycleRuntime();
        var manager = runtime.CreateManager(captureWorkerOutput: true);
        manager.OnWorkerOutput += _ => manager.Dispose();
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        Process process = GetPrivateField<Process>(manager, "_workerProcess");
        DataReceivedEventHandler outputHandler =
            GetProcessHandler<DataReceivedEventHandler>(process, "OutputDataReceived");

        outputHandler(process, CreateDataReceivedEventArgs("dispose"));

        await WaitUntilAsync(
            () => GetPrivateField<object?>(manager, "_workerGeneration") is null
                  && !manager.IsWorkerRunning
                  && IsProcessHandleClosed(process));
        Assert.False(manager.IsWorkerRunning);
        manager.Dispose();
    }

    [Fact]
    public async Task StaleWorkerReloadRequest_DoesNotClaimReloadBeforeGenerationValidation()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager(captureWorkerOutput: true);
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));

        IpcServer oldIpcServer = GetPrivateField<IpcServer>(manager, "_ipcServer");
        Action<IpcMessage> oldMessageHandler = GetMessageHandler(oldIpcServer);
        var transitionGate = GetPrivateField<SemaphoreSlim>(manager, "_transitionGate");
        await transitionGate.WaitAsync();
        try
        {
            oldMessageHandler(new IpcMessage(IpcMessageType.HotReloadRequest));
            await Task.Delay(100);
            Assert.False(manager.IsReloadInProgress);

            Task currentReload = manager.HotReloadAsync();
            await WaitUntilAsync(() => manager.IsReloadInProgress);
            transitionGate.Release();

            Assert.True(await runtime.TryWaitForAsync(
                "state-requested",
                TimeSpan.FromSeconds(2)));
            runtime.ReleaseState();
            await currentReload;
        }
        catch
        {
            if (transitionGate.CurrentCount == 0)
            {
                transitionGate.Release();
            }
            runtime.ReleaseState();
            throw;
        }
    }

    [Fact]
    public async Task ReplacementWorkerReloadRequest_QueuedDuringReloadRunsAfterTransition()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager();
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        runtime.RequestNextWorkerReloadOnStart();

        Task firstReload = manager.HotReloadAsync();
        await runtime.WaitForAsync("state-requested");
        runtime.ReleaseState();
        await firstReload;

        await WaitUntilAsync(() => runtime.StartCount == 3);
        await WaitUntilAsync(() => !manager.IsReloadInProgress);
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task ConnectionFailure_StopsAbandonedReadinessAndReleasesDelegates()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager(
            captureWorkerOutput: true,
            workerConnectionTimeout: TimeSpan.FromMilliseconds(250));
        runtime.ExitNextWorkerBeforeConnect();

        Task start = manager.StartWorkerAsync();
        await WaitUntilAsync(() => GetPrivateField<object?>(manager, "_workerReadiness") is not null);
        Process failedProcess = GetPrivateField<Process>(manager, "_workerProcess");
        Task<bool> readiness = manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Exception? readinessFailure = await Record.ExceptionAsync(
            async () => await readiness.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.IsAssignableFrom<OperationCanceledException>(readinessFailure);
        Assert.Equal(0, ProcessSubscriberCount(failedProcess, "Exited"));
        Assert.Equal(0, ProcessSubscriberCount(failedProcess, "OutputDataReceived"));
        Assert.Equal(0, ProcessSubscriberCount(failedProcess, "ErrorDataReceived"));
        Assert.True(IsProcessHandleClosed(failedProcess));
    }

    [Fact]
    public async Task InvalidWorkerConnectionTimeout_DoesNotPublishAbandonedGeneration()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager(
            workerConnectionTimeout: TimeSpan.FromMilliseconds(-2));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => manager.StartWorkerAsync());

        Assert.Null(GetPrivateField<object?>(manager, "_workerGeneration"));
        Assert.Null(GetPrivateField<object?>(manager, "_workerReadiness"));
        Assert.Null(GetPrivateField<object?>(manager, "_workerProcess"));
        Assert.Null(GetPrivateField<object?>(manager, "_ipcServer"));
        Assert.Null(GetPrivateField<object?>(manager, "_workerMessageHandler"));
    }

    [Fact]
    public async Task StopAfterWorkerAlreadyExited_ReleasesOwnedLifecycleResources()
    {
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager(captureWorkerOutput: true);
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        Process workerProcess = GetPrivateField<Process>(manager, "_workerProcess");

        workerProcess.Kill(entireProcessTree: true);
        Assert.True(await ProcessManager.WaitForExitAsync(workerProcess, TimeSpan.FromSeconds(10)));
        await manager.StopWorkerAsync();

        Assert.Null(GetPrivateField<object?>(manager, "_workerProcess"));
        Assert.Null(GetPrivateField<object?>(manager, "_ipcServer"));
        Assert.Null(GetPrivateField<object?>(manager, "_workerExitNotification"));
        Assert.Null(GetPrivateField<object?>(manager, "_workerReadiness"));
        Assert.Null(GetPrivateField<object?>(manager, "_workerMessageHandler"));
        Assert.Null(GetPrivateField<object?>(manager, "_workerGeneration"));
        Assert.Equal(0, ProcessSubscriberCount(workerProcess, "Exited"));
        Assert.Equal(0, ProcessSubscriberCount(workerProcess, "OutputDataReceived"));
        Assert.Equal(0, ProcessSubscriberCount(workerProcess, "ErrorDataReceived"));
        Assert.True(IsProcessHandleClosed(workerProcess));
    }

    [Fact]
    public async Task DisposeAfterWorkerAlreadyExited_RemovesShadowLeftByFailedExitCleanup()
    {
        if (!OperatingSystem.IsWindows()) return; // FileShare prevents deletion on Windows.
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OnWorkerExited += _ => exited.TrySetResult();
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        Process worker = GetPrivateField<Process>(manager, "_workerProcess");
        string shadow = Path.Combine(runtime.BundlePath, "reload", "shadow", $"{worker.Id}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(shadow);
        using (var locked = new FileStream(Path.Combine(shadow, "Game.dll"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            worker.Kill(entireProcessTree: true);
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(Directory.Exists(shadow));
        }

        manager.Dispose();

        Assert.False(Directory.Exists(shadow));
    }

    [Fact]
    public async Task StopWorker_RetriesShadowDeletionAfterTransientFileLock()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager();
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        Process worker = GetPrivateField<Process>(manager, "_workerProcess");
        string shadow = Path.Combine(runtime.BundlePath, "reload", "shadow", $"{worker.Id}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(shadow);
        Task stop;
        using (var locked = new FileStream(Path.Combine(shadow, "Game.dll"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            stop = manager.StopWorkerAsync();
            await WaitUntilAsync(() => IsProcessHandleClosed(worker));
            await Task.Delay(100);
        }
        await stop;
        Assert.False(Directory.Exists(shadow));
    }

    [Fact]
    public async Task StopWorker_CleansShadowAfterCommittedCallbacksDrain()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var runtime = new LifecycleRuntime();
        using var manager = runtime.CreateManager(captureWorkerOutput: true);
        using var resume = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await manager.StartWorkerAsync();
        Assert.True(await manager.WaitForWorkerReadyAsync(TimeSpan.FromSeconds(10)));
        Process worker = GetPrivateField<Process>(manager, "_workerProcess");
        string shadow = Path.Combine(runtime.BundlePath, "reload", "shadow", $"{worker.Id}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(shadow);
        manager.OnWorkerOutput += line =>
        {
            if (line != "hold-shadow") return;
            using var locked = new FileStream(Path.Combine(shadow, "Game.dll"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            entered.TrySetResult();
            Assert.True(resume.Wait(TimeSpan.FromSeconds(10)));
        };
        GetProcessHandler<DataReceivedEventHandler>(worker, "OutputDataReceived")(
            worker, CreateDataReceivedEventArgs("hold-shadow"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task stop = manager.StopWorkerAsync();
        try
        {
            await WaitUntilAsync(() => GetPrivateField<object?>(manager, "_ipcServer") is null);
            Assert.False(stop.IsCompleted);
        }
        finally
        {
            resume.Set();
        }
        await stop;
        Assert.False(Directory.Exists(shadow));
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
        return ProcessSubscriberCount(process, "Exited");
    }

    private static EventHandler GetExitHandler(Process process)
    {
        return GetProcessHandler<EventHandler>(process, "Exited");
    }

    private static T GetProcessHandler<T>(Process process, string fieldName)
        where T : Delegate
    {
        FieldInfo field = GetProcessDelegateField<T>(fieldName);
        return Assert.IsType<T>(field.GetValue(process));
    }

    private static int ProcessSubscriberCount(Process process, string fieldName)
    {
        FieldInfo field = typeof(Process)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
                typeof(MulticastDelegate).IsAssignableFrom(candidate.FieldType)
                && candidate.Name.Contains(fieldName, StringComparison.OrdinalIgnoreCase));
        return ((MulticastDelegate?)field.GetValue(process))?.GetInvocationList().Length ?? 0;
    }

    private static FieldInfo GetProcessDelegateField<T>(string fieldName)
        where T : Delegate
    {
        return typeof(Process)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
                typeof(T).IsAssignableFrom(candidate.FieldType)
                && candidate.Name.Contains(fieldName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProcessHandleClosed(Process process)
    {
        FieldInfo field = typeof(Process)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
                candidate.FieldType == typeof(Microsoft.Win32.SafeHandles.SafeProcessHandle));
        var handle = (Microsoft.Win32.SafeHandles.SafeProcessHandle?)field.GetValue(process);
        return handle is null || handle.IsClosed;
    }

    private static DataReceivedEventArgs CreateDataReceivedEventArgs(string data)
    {
        return (DataReceivedEventArgs)Activator.CreateInstance(
            typeof(DataReceivedEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [data],
            culture: null)!;
    }

    private static Action<IpcMessage> GetMessageHandler(IpcServer ipcServer)
    {
        FieldInfo field = typeof(IpcServer).GetField(
            "OnMessageReceived",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsType<Action<IpcMessage>>(field.GetValue(ipcServer));
    }

    private static object CreateWorkerReadiness()
    {
        Type readinessType = typeof(ProcessManager).GetNestedType(
            "WorkerReadiness",
            BindingFlags.NonPublic)!;
        return Activator.CreateInstance(readinessType, nonPublic: true)!;
    }

    private static T GetPrivateField<T>(ProcessManager manager, string name)
    {
        return (T)typeof(ProcessManager).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
    }

    private static void SetPrivateField<T>(ProcessManager manager, string name, T value)
    {
        typeof(ProcessManager).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, value);
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

        public int StartCount
        {
            get
            {
                string path = Path.Combine(_controlPath, "starts.log");
                if (!File.Exists(path))
                {
                    return 0;
                }

                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                int count = 0;
                while (reader.ReadLine() is not null)
                {
                    count++;
                }
                return count;
            }
        }

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

        public ProcessManager CreateManager(
            bool captureWorkerOutput = false,
            TimeSpan? workerConnectionTimeout = null)
        {
            var launch = new RuntimeLaunchOptions(Side.Server, RunnerPath, BundlePath, _root);
            return new ProcessManager(
                launch,
                new HotReloadOptions
                {
                    Mode = HotReloadMode.RestartWorker,
                    WorkerConnectionTimeout =
                        workerConnectionTimeout ?? TimeSpan.FromSeconds(10),
                    StateRequestTimeout = TimeSpan.FromSeconds(10),
                    GracefulShutdownTimeout = TimeSpan.FromMilliseconds(250),
                    CaptureWorkerOutput = captureWorkerOutput
                });
        }

        public void ExitNextWorkerBeforeConnect()
        {
            Directory.CreateDirectory(_controlPath);
            File.WriteAllText(Path.Combine(_controlPath, "exit-before-connect"), "exit");
        }

        public void RequestNextWorkerReloadOnStart()
        {
            Directory.CreateDirectory(_controlPath);
            File.WriteAllText(
                Path.Combine(_controlPath, "request-reload-on-start"),
                "reload");
        }

        public void PauseNextStateResponse()
        {
            Directory.CreateDirectory(_controlPath);
            File.WriteAllText(
                Path.Combine(_controlPath, "pause-before-state-response"),
                "pause");
        }

        public void ReleaseStateResponse()
        {
            Directory.CreateDirectory(_controlPath);
            File.WriteAllText(
                Path.Combine(_controlPath, "release-state-response"),
                "release");
        }

        public void RequestReload(int signalWriteDelayMilliseconds = 0)
        {
            Directory.CreateDirectory(_controlPath);
            string trigger = Path.Combine(_controlPath, "request-reload");
            string pending = trigger + ".pending";
            // Publish only after closing the handle: the worker deletes the signal on receipt.
            using (File.Create(pending))
            {
                if (signalWriteDelayMilliseconds > 0)
                {
                    Thread.Sleep(signalWriteDelayMilliseconds);
                }
            }
            File.Move(pending, trigger);
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

        public async Task<bool> TryWaitForAsync(string name, TimeSpan timeout)
        {
            string path = Path.Combine(_controlPath, name);
            using var timeoutSource = new CancellationTokenSource(timeout);
            try
            {
                while (!File.Exists(path))
                {
                    await Task.Delay(10, timeoutSource.Token);
                }
                return true;
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                return false;
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
