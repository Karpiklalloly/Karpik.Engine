using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Collection(nameof(EcsUpdateSchedulerRuntimeCollection))]
public sealed class LuaHotReloadOrchestrationTests
{
    [Fact]
    public void ReloadRequiresExplicitLocalOptIn()
    {
        using var f = new Fixture(false);
        Assert.Equal(SimulationCodeReloadStatus.Unsupported, f.Reload().Status);
        Assert.Empty(f.Preparation.Events);
    }

    [Theory]
    [InlineData(EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(EcsUpdateSchedulerMode.Parallel)]
    [InlineData(EcsUpdateSchedulerMode.SingleThread)]
    public void ReloadPublishesCandidateOnlyAfterGraphValidation(EcsUpdateSchedulerMode mode)
    {
        using var f = new Fixture(true, mode);
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(1, f.Preparation.Old.Calls);
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Reload().Status);
        Assert.Equal(new[] { "prepare", "activation", "activate", "retire", "dispose" }, f.Preparation.Events);
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(1, f.Preparation.Old.Calls);
        Assert.Equal(1, f.Preparation.Next.Calls);
    }

    [Fact]
    public void InvalidGraphRejectsWithoutCallingActivationOrStoppingOldSystems()
    {
        using var f = new Fixture(true);
        f.Preparation.InvalidGraph = true;
        Assert.Equal(SimulationCodeReloadStatus.Rejected, f.Reload().Status);
        Assert.Equal(new[] { "prepare", "dispose" }, f.Preparation.Events);
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(1, f.Preparation.Old.Calls);
        Assert.Equal(0, f.Preparation.Next.Calls);
        Assert.True(f.Runner.IsApplicationRunning);
    }

    [Fact]
    public void CancellationBeforeActivationRejectsCandidate()
    {
        using var f = new Fixture(true);
        using var cancellation = new CancellationTokenSource();
        f.Preparation.OnPrepare = cancellation.Cancel;
        Assert.Equal(SimulationCodeReloadStatus.Cancelled, f.Reload(cancellation.Token).Status);
        Assert.Equal(new[] { "prepare", "dispose" }, f.Preparation.Events);
        Assert.True(f.Runner.IsApplicationRunning);
    }

    [Fact]
    public void CleanupIntegrityFailureStopsSimulation()
    {
        using var f = new Fixture(true);
        f.Preparation.InvalidGraph = true;
        f.Preparation.CleanupFailure = true;
        Assert.Equal(SimulationCodeReloadStatus.Faulted, f.Reload().Status);
        Assert.False(f.Runner.IsApplicationRunning);
    }

    [Fact]
    public void CancellationAfterCommitDoesNotRollback()
    {
        using var f = new Fixture(true);
        using var cancellation = new CancellationTokenSource();
        f.Preparation.OnActivate = cancellation.Cancel;
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Reload(cancellation.Token).Status);
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(1, f.Preparation.Next.Calls);
    }

    [Fact]
    public async Task ConcurrentRequestIsBusyUntilRetirementFinishes()
    {
        using var f = new Fixture(true);
        Task<SimulationCodeReloadResult>? second = null;
        f.Preparation.OnPrepare = () => second = f.Runner.RequestCodeReloadAsync();
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Reload().Status);
        Assert.Equal(SimulationCodeReloadStatus.Busy, (await second!).Status);
    }

    [Fact]
    public async Task RealClientBoundary_WaitsForInFlightFrame_ThenPublishesBeforeWorkerResumes()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var f = new Fixture(true, side: Side.Client);
        using var worker = new ClientSimulationWorker(new RunnerLoop(f.Runner));
        f.Runner.SetCodeReloadBoundary(new Karpik.Engine.Core.Runner.WorkerHost.ClientCodeReloadBoundary(worker, f.Scheduler));
        f.Preparation.Old.OnUpdate = () => { entered.Set(); release.Wait(); };
        Assert.True(worker.TryReserveFrame());
        worker.StartReservedFrame(.02);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task<SimulationCodeReloadResult> request = f.Runner.RequestCodeReloadAsync();
            f.Scheduler.Execute();
            Assert.True(f.Runner.IsCodeReloading);
            Assert.False(request.IsCompleted);
            Assert.Empty(f.Preparation.Events);
            Assert.Throws<InvalidOperationException>(() => f.Runner.GetHotReloadData());
            Assert.Throws<InvalidOperationException>(() => f.Runner.CaptureEditorSnapshot());
            release.Set();
            f.Scheduler.RunUntilCompleted(request);
            Assert.Equal(SimulationCodeReloadStatus.Success, (await request).Status);
            int calls = await worker.InvokeAsync(() => { f.Runner.RunGameplayFrame(.02); return f.Preparation.Next.Calls; });
            Assert.Equal(1, calls);
            Assert.Equal(1, f.Preparation.Old.Calls);
        }
        finally { release.Set(); }
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1031", Justification = "Drives the main-thread scheduler and reads only completed requests.")]
    public void AsyncPreparation_KeepsFramesPaused_AndCancellationCleansCandidate()
    {
        using var f = new Fixture(true);
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Preparation.PreparationGate = gate.Task;
        Task<SimulationCodeReloadResult> first = f.Runner.RequestCodeReloadAsync(cancellation.Token);
        f.Scheduler.Execute();
        Assert.False(first.IsCompleted);
        Assert.Equal(SimulationCodeReloadStatus.Busy, f.Runner.RequestCodeReloadAsync().GetAwaiter().GetResult().Status);
        f.Runner.RunGameplayFrame(.02);
        f.Runner.RunRender();
        Assert.Equal(0, f.Preparation.Old.Calls);
        cancellation.Cancel();
        gate.SetResult();
        f.Scheduler.RunUntilCompleted(first);
        Assert.Equal(SimulationCodeReloadStatus.Cancelled, first.GetAwaiter().GetResult().Status);
        Assert.Equal(new[] { "prepare", "dispose" }, f.Preparation.Events);
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(1, f.Preparation.Old.Calls);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1031", Justification = "Drives the main-thread scheduler and reads completed requests after draining shutdown.")]
    public void Shutdown_DrainsNonCooperativePreparation_BeforeDisposingScopes()
    {
        using var f = new Fixture(true);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Preparation.PreparationGate = gate.Task;
        Task<SimulationCodeReloadResult> request = f.Runner.RequestCodeReloadAsync();
        f.Scheduler.Execute();
        Task shutdown = f.Runner.DestroyAsync();
        Assert.False(shutdown.IsCompleted);
        Assert.False(request.IsCompleted);
        gate.SetResult();
        f.Scheduler.RunUntilCompleted(shutdown);
        Assert.Equal(SimulationCodeReloadStatus.Cancelled, request.GetAwaiter().GetResult().Status);
        Assert.Contains("dispose", f.Preparation.Events);
    }

    [Fact]
    public async Task ExplicitlyEnabledRequest_RejectsBeforeStartupAndAfterShutdown()
    {
        var runner = new EngineRunner(NullLoggerFactory.Instance);
        runner.EnableLocalCodeReload();
        Assert.Equal(SimulationCodeReloadStatus.InvalidState, (await runner.RequestCodeReloadAsync()).Status);
        await runner.DestroyAsync();
        Assert.Equal(SimulationCodeReloadStatus.InvalidState, (await runner.RequestCodeReloadAsync()).Status);
    }

    [Fact]
    public async Task ShutdownRequest_ClosesFrameEntryBeforeScopeDisposal()
    {
        using var f = new Fixture(true);
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(1, f.Preparation.Old.Calls);
        await f.Runner.DrainCodeReloadForShutdownAsync();
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(1, f.Preparation.Old.Calls);
    }

    [Fact]
    public void Shutdown_WaitsForCurrentClientFrameBeforeDisposingSimulationResources()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var f = new Fixture(true, side: Side.Client);
        using var worker = new ClientSimulationWorker(new RunnerLoop(f.Runner));
        f.Preparation.Old.OnUpdate = () => { entered.Set(); release.Wait(); };
        Assert.True(worker.TryReserveFrame());
        worker.StartReservedFrame(.02);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task stopping = f.Runner.DestroyAsync();
            Assert.False(stopping.IsCompleted);
            Assert.False(f.Preparation.Disposed);
            release.Set();
            f.Scheduler.RunUntilCompleted(stopping);
            Assert.True(f.Preparation.Disposed);
            f.Runner.RunGameplayFrame(.02);
            Assert.Equal(1, f.Preparation.Old.Calls);
        }
        finally { release.Set(); }
    }

    private sealed class RunnerLoop(EngineRunner runner) : IClientSimulationLoop
    {
        public bool IsApplicationRunning => runner.IsApplicationRunning;
        public void RunGameplayFrame(double deltaTime) => runner.RunGameplayFrame(deltaTime);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly EngineRunner Runner = new(NullLoggerFactory.Instance);
        public readonly MainThreadScheduler Scheduler = new(Environment.CurrentManagedThreadId, NullLoggerFactory.Instance);
        public readonly Preparation Preparation = new();
        public Fixture(bool optIn, EcsUpdateSchedulerMode mode = EcsUpdateSchedulerMode.Deterministic, Side side = Side.Server)
        {
            Runner.UpdateSchedulerMode = mode;
            if (optIn) Runner.EnableLocalCodeReload();
            Runner.RegisterModule(new Installer(Preparation));
            Runner.Setup(new Application(side), Scheduler);
            Scheduler.Execute();
        }
        public SimulationCodeReloadResult Reload(CancellationToken token = default)
        {
            var task = Runner.RequestCodeReloadAsync(token);
            Scheduler.RunUntilCompleted(task);
            return task.GetAwaiter().GetResult();
        }
        public void Dispose() { Runner.Destroy(); Scheduler.Dispose(); }
    }

    [Module(ModuleScope.Simulation)]
    private sealed class Installer(Preparation preparation) : IModuleInstaller
    {
        public string Name => "ReloadProbe";
        public void OnRegisterServices(ContainerBuilder builder) =>
            builder.Register(context => preparation.Bind(context.Resolve<ISystemRuntimeRegistry>()))
                .As<ISimulationPreparation>().SingleInstance();
    }

    public sealed class Counter : ISystemUpdate
    {
        public int Calls;
        public Action? OnUpdate;
        public void Update() { OnUpdate?.Invoke(); Calls++; }
    }

    private sealed class Preparation : ISimulationPreparation, ISimulationCodeReloadParticipant, IDisposable
    {
        public readonly Counter Old = new(), Next = new();
        public readonly List<string> Events = [];
        public bool InvalidGraph, CleanupFailure;
        public Action? OnPrepare, OnActivate;
        public Task? PreparationGate;
        public bool Disposed;
        public void Dispose() => Disposed = true;
        private ISystemRuntimeRegistry _registry = null!;
        public ReadOnlyMemory<ISystemUpdate> ActiveSystems => new ISystemUpdate[] { Old };
        public Preparation Bind(ISystemRuntimeRegistry registry) { _registry = registry; return this; }
        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            _registry.AddInstance(Old, Descriptor("probe:old"));
            return ValueTask.CompletedTask;
        }
        public async ValueTask<ISimulationCodeReloadCandidate> PrepareReloadAsync(CancellationToken cancellationToken)
        {
            Events.Add("prepare"); OnPrepare?.Invoke();
            if (PreparationGate is not null) await PreparationGate;
            return new Candidate(this);
        }
        private static EcsUpdateSystemDescriptor Descriptor(string id) => new(typeof(Counter), true, Array.Empty<EcsComponentAccessDescriptor>(), Array.Empty<EcsSystemOrderDescriptor>(), id);
        private sealed class Candidate(Preparation owner) : ISimulationCodeReloadCandidate
        {
            public ReadOnlyMemory<ISystemUpdate> Systems => new ISystemUpdate[] { owner.Next };
            public ReadOnlyMemory<EcsUpdateSystemDescriptor> Descriptors => owner.InvalidGraph
                ? new[] { Descriptor("duplicate"), Descriptor("duplicate") } : new[] { Descriptor("probe:next") };
            public void PrepareActivation(CancellationToken cancellationToken) => owner.Events.Add("activation");
            public void Activate() { owner.Events.Add("activate"); owner.OnActivate?.Invoke(); }
            public void RetirePrevious() => owner.Events.Add("retire");
            public void Dispose()
            {
                owner.Events.Add("dispose");
                if (owner.CleanupFailure) throw new SimulationCodeReloadCleanupException("Host cleanup failed", new InvalidOperationException());
            }
        }
    }
}
