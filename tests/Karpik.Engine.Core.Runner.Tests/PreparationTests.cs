using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Xunit;

[Collection(nameof(EcsUpdateSchedulerRuntimeCollection))]
public sealed class PreparationTests
{
    [Fact]
    public void ModuleRegistry_DoesNotExposeRuntimeRegistration()
    {
        Assert.DoesNotContain(typeof(ISystemRegistry).GetMethods(), method => method.Name == "AddInstance");
    }

    [Theory]
    [InlineData(false, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(false, EcsUpdateSchedulerMode.Parallel)]
    [InlineData(true, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(true, EcsUpdateSchedulerMode.Parallel)]
    public async Task Setup_PreparesScopesBeforeSystems_AndClosesRuntimeRegistration(bool useStatic, EcsUpdateSchedulerMode mode)
    {
        var state = new State();
        var runner = CreateRunner(state, useStatic);
        runner.UpdateSchedulerMode = mode;
        using var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        try
        {
            Task setup = runner.SetupAsync(new Application(Side.Server), scheduler);
            scheduler.Execute();
            await setup;
            Assert.Equal(["ModSet", "Simulation.Construct", "Simulation", "Init", "AsyncInit"], state.Events);
            Assert.Throws<InvalidOperationException>(() => state.Registry!.AddInstance("late", new RuntimeProbe(state)));
            runner.RunGameplayFrame(0.001);
            Assert.Equal("Update", state.Events[^1]);
        }
        finally { await runner.DestroyAsync(); }
        Assert.Equal(["Simulation.Dispose", "ModSet.Dispose"], state.Events.TakeLast(2));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Setup_PreparationFailure_DisposesPreparedScopesWithoutInitializingSystems(bool useStatic, bool failModSet)
    {
        var state = new State { FailureStage = failModSet ? "ModSet" : "Simulation" };
        var runner = CreateRunner(state, useStatic);
        using var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        Task setup = runner.SetupAsync(new Application(Side.Server), scheduler);
        scheduler.Execute();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => setup);
        Assert.Equal("preparation failed", error.Message);
        Assert.DoesNotContain("Init", state.Events);
        Assert.Equal(failModSet ? ["ModSet.Dispose"] : new[] { "Simulation.Dispose", "ModSet.Dispose" },
            state.Events.Where(item => item.EndsWith(".Dispose")));
        Assert.Equal(1, state.Events.Count(item => item == "ModSet.Dispose"));
        state.FailureStage = null;
        state.Events.Clear();
        Task retry = runner.SetupAsync(new Application(Side.Server), scheduler);
        scheduler.Execute();
        try
        {
            await retry;
            Assert.Equal("AsyncInit", state.Events[^1]);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Theory]
    [InlineData(false, "ModSet")]
    [InlineData(false, "Simulation")]
    [InlineData(true, "ModSet")]
    [InlineData(true, "Simulation")]
    public async Task Setup_AwaitsPreparation_AndCancellationCleansUp(bool useStatic, string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var state = new State { WaitStage = stage };
        var runner = CreateRunner(state, useStatic);
        using var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        Task setup = runner.SetupAsync(new Application(Side.Server), scheduler, cancellationToken: cancellation.Token);
        scheduler.Execute();
        Assert.False(setup.IsCompleted);
        Assert.DoesNotContain("Init", state.Events);
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => scheduler.RunUntilCompleted(setup));
        Assert.Equal("ModSet.Dispose", state.Events[^1]);
        Assert.DoesNotContain("Init", state.Events);
        await runner.DestroyAsync();
    }

    [Theory]
    [InlineData(false, "ModSet")]
    [InlineData(false, "Simulation")]
    [InlineData(true, "ModSet")]
    [InlineData(true, "Simulation")]
    public async Task Setup_ResumesAfterAsynchronousPreparation(bool useStatic, string stage)
    {
        var state = new State { WaitStage = stage };
        var runner = CreateRunner(state, useStatic);
        using var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        try
        {
            Task setup = runner.SetupAsync(new Application(Side.Server), scheduler);
            scheduler.Execute();
            Assert.False(setup.IsCompleted);
            state.Gate.SetResult();
            scheduler.RunUntilCompleted(setup);
            await setup;
            Assert.Equal("AsyncInit", state.Events[^1]);
        }
        finally { await runner.DestroyAsync(); }
    }

    [Fact]
    public void SynchronousSetup_RejectsAsyncPreparationWithoutDisposingInFlightServices()
    {
        var state = new State { WaitStage = "ModSet", IgnoreCancellation = true };
        var runner = CreateRunner(state, false);
        using var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        runner.Setup(new Application(Side.Server), scheduler);
        try
        {
            Assert.Throws<InvalidOperationException>(() => scheduler.Execute());
            Assert.DoesNotContain("ModSet.Dispose", state.Events);
            Assert.Throws<InvalidOperationException>(() => { _ = runner.SetupAsync(new Application(Side.Server), scheduler); });
        }
        finally
        {
            state.Gate.TrySetResult();
            Assert.True(SpinWait.SpinUntil(() =>
            {
                scheduler.Execute();
                return state.Disposed.Task.IsCompleted;
            }, TimeSpan.FromSeconds(5)));
            runner.Destroy();
        }
        Assert.DoesNotContain("Init", state.Events);
        Assert.DoesNotContain("Simulation.Construct", state.Events);
    }

    private static EngineRunner CreateRunner(State state, bool useStatic)
    {
        var runner = new EngineRunner();
        if (useStatic)
        {
            runner.RegisterStaticComposition(new Composition(state));
            runner.AttributedServiceRegistrationProbe += (_, _, _) => throw new Exception("Static used reflection discovery");
        }
        else
        {
            runner.RegisterModule(new ModSetInstaller(state));
            runner.RegisterModule(new SimulationInstaller(state));
        }
        return runner;
    }

    internal sealed class State
    {
        public List<string> Events { get; } = [];
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int OwnerThreadId { get; } = Environment.CurrentManagedThreadId;
        public bool IgnoreCancellation { get; init; }
        public string? FailureStage { get; set; }
        public string? WaitStage { get; init; }
        public ISystemRuntimeRegistry? Registry { get; set; }
        public bool ModSetPrepared { get; set; }

        public async ValueTask Prepare(string stage, CancellationToken token)
        {
            Events.Add(stage);
            if (WaitStage == stage) await (IgnoreCancellation ? Gate.Task : Gate.Task.WaitAsync(token));
            if (FailureStage == stage) throw new InvalidOperationException("preparation failed");
        }
    }

    private sealed class ModSetPreparation(State state, IServiceResolver services) : IModSetPreparation, IDisposable
    {
        public async ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            Assert.Null(services.GetService(typeof(ISystemRuntimeRegistry)));
            await state.Prepare("ModSet", cancellationToken);
            state.ModSetPrepared = true;
        }
        public void Dispose()
        {
            state.Events.Add("ModSet.Dispose");
            state.Disposed.TrySetResult();
        }
    }

    private sealed class SimulationPreparation : ISimulationPreparation, IDisposable
    {
        private readonly State _state;
        public SimulationPreparation(State state, ISystemRuntimeRegistry systems)
        {
            Assert.Equal(state.OwnerThreadId, Environment.CurrentManagedThreadId);
            Assert.True(state.ModSetPrepared);
            _state = state;
            state.Registry = systems;
            state.Events.Add("Simulation.Construct");
        }
        public async ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            _state.Registry!.AddInstance(new RuntimeProbe(_state),
                new EcsUpdateSystemDescriptor(typeof(RuntimeProbe), true, default, default, "mod:runtime"));
            await _state.Prepare("Simulation", cancellationToken);
        }
        public void Dispose() => _state.Events.Add("Simulation.Dispose");
    }

    internal sealed class RuntimeProbe(State state) : ISystemUpdate
    {
        public void Update() => state.Events.Add("Update");
    }

    private sealed class InitProbe(State state) : ISystemInit, ISystemAsyncInit
    {
        public void Init()
        {
            Assert.Equal(state.OwnerThreadId, Environment.CurrentManagedThreadId);
            state.Events.Add("Init");
        }
        public ValueTask InitAsync(CancellationToken cancellationToken)
        {
            state.Events.Add("AsyncInit");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProbeModule : IModule
    {
        public void Add(ISystemRegistry systems) => systems.Add<InitProbe>();
    }

    [Module(ModuleScope.ModSet)]
    private sealed class ModSetInstaller(State state) : IModuleInstaller
    {
        public string Name => nameof(ModSetInstaller);
        public void OnRegisterServices(ContainerBuilder builder) => builder.Register(context =>
            new ModSetPreparation(state, context.Resolve<IServiceResolver>())).As<IModSetPreparation>().SingleInstance();
    }

    [Module(ModuleScope.Simulation)]
    private sealed class SimulationInstaller(State state, bool registerServices = true) : IModuleInstaller
    {
        public string Name => nameof(SimulationInstaller);
        public IModule CreateModule() => new ProbeModule();
        public void OnRegisterServices(ContainerBuilder builder)
        {
            if (!registerServices) return;
            builder.RegisterInstance(state).AsSelf();
            builder.Register(context => new SimulationPreparation(state, context.Resolve<ISystemRuntimeRegistry>()))
                .As<ISimulationPreparation>().SingleInstance();
        }
    }

    private sealed class Composition(State state) : IStaticRuntimeComposition
    {
        public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry) { }
        public void RegisterModules(IStaticModuleRegistry registry) => registry.Add(new SimulationInstaller(state, false));
        public void RegisterServices(IStaticServiceRegistry registry)
        {
            registry.Register<IModSetPreparation, ModSetPreparation>(ModuleScope.ModSet, ServiceLifetime.Singleton,
                services => new ModSetPreparation(state, services));
            registry.Register<ISimulationPreparation, SimulationPreparation>(ModuleScope.Simulation, ServiceLifetime.Singleton,
                services => new SimulationPreparation(state, services.Resolve<ISystemRuntimeRegistry>()));
            registry.Register<InitProbe, InitProbe>(ModuleScope.Simulation, ServiceLifetime.Singleton, _ => new InitProbe(state));
        }
    }
}
