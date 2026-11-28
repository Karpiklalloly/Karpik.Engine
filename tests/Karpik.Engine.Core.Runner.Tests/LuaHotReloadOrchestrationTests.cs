using Autofac;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
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

    [Fact]
    public void NetworkCompositionBlocksIndependentReloadEvenWithLocalOptIn()
    {
        using var f = new Fixture(true, networked: true);
        Assert.Equal(SimulationCodeReloadStatus.Unsupported, f.Reload().Status);
        Assert.Empty(f.Preparation.Events);
    }

#if DEBUG
    [Fact]
    public void SchemaReloadReservesCaptureAndHoldsTheExistingLeaseUntilResume()
    {
        using var f = new Fixture(true, schemaReload: true);
        var host = Assert.IsAssignableFrom<ISimulationSchemaReloadHost>(f.Runner);
        var boundary = new LiveBoundary();
        f.Runner.SetCodeReloadBoundary(boundary);
        Assert.Equal(SimulationCodeReloadStatus.Success, host.TryReserve(1, 5, 6, out var handle));
        var reload = Assert.IsAssignableFrom<ISimulationSchemaReload>(handle);
        Assert.Throws<InvalidOperationException>(() => f.Runner.GetHotReloadData());
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(1, f.Preparation.Old.Calls); // Reservation does not stop frames during drain.
        Assert.False(boundary.Held);
        Assert.True(reload.TryPrepare(digest => digest == "probe"));
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Finish(reload.Ready).Status);
        Assert.Equal(32, reload.SharedSchemaProof.Length);
        Assert.True(boundary.Held);
        Assert.Equal(new[] { "prepare", "activation" }, f.Preparation.Events);
        Assert.True(reload.TryCommit());
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Finish(reload.Committed).Status);
        Assert.True(boundary.Held);
        Assert.True(reload.TryResume());
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Finish(reload.Completion).Status);
        Assert.False(boundary.Held);
        Assert.Throws<InvalidOperationException>(() => f.Runner.GetHotReloadData()); // Await network settlement too.
        reload.Release();
        Assert.NotNull(f.Runner.GetHotReloadData());
    }
#endif

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

    private static readonly ModLiveTransition.Operation LiveOperation = new(1, 0, 1);

    [Fact]
    public void LiveReloadRequiresInternalHostOptIn()
    {
        using var f = new Fixture(true);
        Assert.Equal(SimulationCodeReloadStatus.Unsupported, f.Runner.BeginLiveModReload(LiveOperation, [], out _));
        Assert.Empty(f.Preparation.Events);
    }

    [Fact]
    public void LiveReloadHoldsLeaseCaptureRefreshAndVirtualTimeUntilResume()
    {
        using var f = new Fixture(false, live: true);
        var boundary = new LiveBoundary();
        f.Runner.SetCodeReloadBoundary(boundary);
        f.Runner.RunGameplayFrame(.02);
        double before = f.Preparation.Time.TotalTime;
        var live = f.BeginLive();
        Assert.False(f.Command(ModLiveTransition.Command.Commit));
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Finish(live.Ready).Status);
        Assert.True(boundary.Held);
        Assert.Equal(new[] { "prepare", "activation" }, f.Preparation.Events);
        Assert.False(f.Runner.TryApplyLiveModCommand(LiveOperation with { Target = 2 }, ModLiveTransition.Command.Commit));
        Assert.False(f.Command(ModLiveTransition.Command.Resume));
        Assert.True(f.Command(ModLiveTransition.Command.Commit));
        Assert.False(f.Command(ModLiveTransition.Command.Commit));
        Assert.False(f.Command(ModLiveTransition.Command.Abort));
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Finish(live.Committed).Status);
        Assert.Equal(new[] { "prepare", "activation", "activate", "retire", "dispose" }, f.Preparation.Events);
        Assert.True(boundary.Held);
        Assert.False(live.Completion.IsCompleted);
        Assert.Equal(SimulationCodeReloadStatus.Busy, f.Runner.BeginLiveModReload(LiveOperation with { Id = 2 }, [], out _));
        Assert.Equal(ModContentRefreshStatus.Busy, f.Finish(f.Runner.RequestModContentRefreshAsync()).Status);
        Assert.Throws<InvalidOperationException>(() => f.Runner.GetHotReloadData());
        Assert.Throws<InvalidOperationException>(() => f.Runner.CaptureEditorSnapshot());
        f.Runner.RunGameplayFrame(100);
        Assert.Equal(0, f.Preparation.Next.Calls);
        Assert.Equal(before, f.Preparation.Time.TotalTime);

        f.Preparation.Time.IsPaused = true;
        Assert.True(f.Command(ModLiveTransition.Command.Resume));
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Finish(live.Completion).Status);
        Assert.False(boundary.Held);
        Assert.False(f.Command(ModLiveTransition.Command.Resume));
        Assert.Equal(SimulationCodeReloadStatus.InvalidState, f.Runner.BeginLiveModReload(LiveOperation with { Id = 2 }, [], out _));
        Assert.Empty(f.Runner.GetHotReloadData());
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(1, f.Preparation.Next.Calls);
        Assert.True(f.Preparation.Time.IsPaused);
        Assert.Equal(before, f.Preparation.Time.TotalTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveReadyShutdownJoinsTheHostDecision(bool channelLost)
    {
        using var f = new Fixture(false, live: true);
        var boundary = new LiveBoundary();
        f.Runner.SetCodeReloadBoundary(boundary);
        var live = f.BeginLive();
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Finish(live.Ready).Status);
        Task stopping = f.Runner.DrainCodeReloadForShutdownAsync();
        f.Scheduler.Execute();
        Assert.False(stopping.IsCompleted);
        Assert.True(boundary.Held);
        Assert.True(f.Command(channelLost ? ModLiveTransition.Command.FailStop : ModLiveTransition.Command.Abort));
        f.Scheduler.RunUntilCompleted(stopping);
        Assert.Equal(channelLost ? SimulationCodeReloadStatus.Faulted : SimulationCodeReloadStatus.Cancelled, f.Finish(live.Completion).Status);
        Assert.Equal(new[] { "prepare", "activation", "dispose" }, f.Preparation.Events);
        Assert.False(boundary.Held);
        Assert.Equal(!channelLost, f.Runner.IsApplicationRunning);
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(0, f.Preparation.Old.Calls);
    }

    [Fact]
    public void LiveCommitFollowedByShutdownCannotResumeOrRollback()
    {
        using var f = new Fixture(false, live: true);
        var boundary = new LiveBoundary();
        f.Runner.SetCodeReloadBoundary(boundary);
        var live = f.BeginLive();
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Finish(live.Ready).Status);
        Task stopping = f.Runner.DrainCodeReloadForShutdownAsync();
        Assert.True(f.Command(ModLiveTransition.Command.Commit));
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Finish(live.Committed).Status);
        Assert.False(stopping.IsCompleted);
        Assert.False(f.Command(ModLiveTransition.Command.Resume));
        Assert.False(f.Command(ModLiveTransition.Command.Abort));
        Assert.True(f.Command(ModLiveTransition.Command.FailStop));
        f.Scheduler.RunUntilCompleted(stopping);
        Assert.Equal(SimulationCodeReloadStatus.Faulted, f.Finish(live.Completion).Status);
        Assert.Equal(new[] { "prepare", "activation", "activate", "retire", "dispose" }, f.Preparation.Events);
        Assert.False(boundary.Held);
        Assert.False(f.Runner.IsApplicationRunning);
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(0, f.Preparation.Next.Calls);
    }

    [Fact]
    public void LiveFailStopBeforeOwnerPublicationSkipsActivation()
    {
        using var f = new Fixture(false, live: true);
        var boundary = new LiveBoundary();
        f.Runner.SetCodeReloadBoundary(boundary);
        var live = f.BeginLive();
        Assert.Equal(SimulationCodeReloadStatus.Success, f.Finish(live.Ready).Status);

        // The owner does not pump between the two commands, so activation cannot start first.
        Assert.True(f.Command(ModLiveTransition.Command.Commit));
        Assert.True(f.Command(ModLiveTransition.Command.FailStop));
        Assert.Equal(SimulationCodeReloadStatus.Faulted, f.Finish(live.Completion).Status);
        Assert.Equal(new[] { "prepare", "activation", "dispose" }, f.Preparation.Events);
        Assert.Equal(SimulationCodeReloadStatus.Faulted, f.Finish(live.Committed).Status);
        Assert.False(boundary.Held);
        Assert.False(f.Runner.IsApplicationRunning);
        f.Runner.RunGameplayFrame(.02);
        Assert.Equal(0, f.Preparation.Old.Calls);
        Assert.Equal(0, f.Preparation.Next.Calls);
    }

    private sealed class LiveBoundary : ISimulationCodeReloadBoundary, IDisposable
    {
        internal bool Held;
        public ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
        {
            Assert.False(Held);
            Held = true;
            return ValueTask.FromResult<IDisposable>(this);
        }
        public void Dispose() => Held = false;
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
        public Fixture(bool optIn, EcsUpdateSchedulerMode mode = EcsUpdateSchedulerMode.Deterministic, Side side = Side.Server,
            bool live = false, bool networked = false, bool schemaReload = false)
        {
            Runner.UpdateSchedulerMode = mode;
            if (optIn) Runner.EnableLocalCodeReload();
            if (live) Runner.EnableHostModLiveReload();
#if DEBUG
            if (schemaReload) Runner.EnableDebugSchemaReload(5);
#endif
            if (networked) Runner.RegisterModule(new Karpik.Engine.Shared.Network.LiteNetLib.NetworkModuleInstaller());
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
        internal EngineRunner.LiveModReload BeginLive()
        {
            Assert.Equal(SimulationCodeReloadStatus.Success, Runner.BeginLiveModReload(LiveOperation, [], out var live));
            return Assert.IsType<EngineRunner.LiveModReload>(live);
        }
        internal bool Command(ModLiveTransition.Command command) => Runner.TryApplyLiveModCommand(LiveOperation, command);
        internal T Finish<T>(Task<T> task)
        {
            Scheduler.RunUntilCompleted(task);
            return task.GetAwaiter().GetResult();
        }
        public void Dispose()
        {
            Command(ModLiveTransition.Command.FailStop);
            Runner.Destroy();
            Scheduler.Dispose();
        }
    }

    [Module(ModuleScope.Simulation)]
    private sealed class Installer(Preparation preparation) : IModuleInstaller
    {
        public string Name => "ReloadProbe";
        public void OnRegisterServices(ContainerBuilder builder) =>
            builder.Register(context => preparation.Bind(context.Resolve<ISystemRuntimeRegistry>(), context.Resolve<Time>()))
                .As<ISimulationPreparation>().SingleInstance();
    }

    public sealed class Counter : ISystemUpdate
    {
        public int Calls;
        public Action? OnUpdate;
        public void Update() { OnUpdate?.Invoke(); Calls++; }
    }

    private sealed class Preparation : ISimulationPreparation, ISimulationLiveModParticipant, IDisposable
#if DEBUG
        , ISimulationSchemaReloadParticipant
#endif
    {
#if DEBUG
        public bool SupportsSchemaReload => true;
        public async ValueTask<IPreparedSchemaReload> PrepareSchemaReloadAsync(Func<string, bool> admission, CancellationToken token) =>
            admission("probe") ? (IPreparedSchemaReload)await PrepareReloadAsync(token) : throw new UnauthorizedAccessException();
#endif
        public readonly Counter Old = new(), Next = new();
        public readonly List<string> Events = [];
        public bool InvalidGraph, CleanupFailure;
        public Action? OnPrepare, OnActivate;
        public Task? PreparationGate;
        public bool Disposed;
        public void Dispose() => Disposed = true;
        private ISystemRuntimeRegistry _registry = null!;
        public ReadOnlyMemory<ISystemUpdate> ActiveSystems => new ISystemUpdate[] { Old };
        public Time Time = null!;
        public Preparation Bind(ISystemRuntimeRegistry registry, Time time) { _registry = registry; Time = time; return this; }
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
        public ModContentRefreshCapture? CreateLiveModRefreshCapture(Action<ContentDiagnostic> report) => null;
        public async ValueTask<ISimulationCodeReloadCandidate?> PrepareLiveModAsync(ContentReloadCoordinator? coordinator,
            IReadOnlyCollection<string> selectedIds, Action<ContentDiagnostic> report, CancellationToken cancellationToken) =>
            await PrepareReloadAsync(cancellationToken);
        private static EcsUpdateSystemDescriptor Descriptor(string id) => new(typeof(Counter), true, Array.Empty<EcsComponentAccessDescriptor>(), Array.Empty<EcsSystemOrderDescriptor>(), id);
        private sealed class Candidate(Preparation owner) : ISimulationCodeReloadCandidate
#if DEBUG
            , IPreparedSchemaReload
#endif
        {
#if DEBUG
            public ReadOnlyMemory<byte> SharedSchemaProof { get; } = new byte[32];
#endif
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
