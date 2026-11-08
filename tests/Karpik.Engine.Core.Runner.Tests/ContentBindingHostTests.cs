using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Autofac;
using Xunit;

public sealed class ContentBindingHostTests
{
    [Fact]
    public async Task Boundary_advances_each_owner_and_stops_all_before_disposal()
    {
        var trace = new List<string>();
        var first = new Owner("first", trace);
        var second = new Owner("second", trace);
        var system = new ContentBindingBoundarySystem([first, second]);
        system.MainThreadFrameBegin();
        Assert.Equal(1, first.Frames);
        Assert.Equal(1, second.Frames);
        await system.DestroyAsync();
        await system.DestroyAsync();
        system.MainThreadFrameBegin();
        Assert.Equal(1, first.Frames);
        Assert.Equal(["stop:first", "stop:second", "dispose:first", "dispose:second"], trace);
    }

    [Fact]
    public async Task Shutdown_attempts_other_owners_after_one_fails()
    {
        var trace = new List<string>();
        var system = new ContentBindingBoundarySystem([
            new Owner("first", trace) { FailDispose = true }, new Owner("second", trace)]);
        await Assert.ThrowsAsync<AggregateException>(() => system.DestroyAsync().AsTask());
        Assert.Contains("dispose:second", trace);
    }

    [Fact]
    public async Task Warm_boundary_has_zero_caller_allocations()
    {
        var system = new ContentBindingBoundarySystem([new Owner("test", [])]);
        for (int i = 0; i < 100; i++) system.MainThreadFrameBegin();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) system.MainThreadFrameBegin();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        await system.DestroyAsync();
    }

    [Fact]
    public async Task Static_host_automatically_pumps_scoped_owners_without_reload_opt_in()
    {
        var trace = new List<string>();
        var owner = new Owner("host", trace);
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        var runner = new EngineRunner();
        runner.RegisterStaticComposition(new Composition(owner));
        runner.Setup(new Application(Side.Server), scheduler);
        scheduler.Execute();
        runner.RunMainThreadFrameBegin();
        Assert.Equal(1, owner.Frames);
        await runner.DestroyAsync();
        Assert.Equal(["stop:host", "dispose:host"], trace);
    }

    private sealed class Owner(string name, List<string> trace) : IContentBindingRuntime
    {
        public int Frames;
        public bool FailDispose;
        private bool _stopped;
        private bool _disposed;
        public void AdvanceBoundary() => Frames++;
        public void RequestStop()
        {
            if (_stopped) return;
            _stopped = true;
            trace.Add("stop:" + name);
        }
        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            trace.Add("dispose:" + name);
            return FailDispose ? ValueTask.FromException(new InvalidOperationException("test cleanup")) : ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Dynamic_host_automatically_pumps_explicit_scoped_owners()
    {
        var trace = new List<string>();
        var owner = new Owner("dynamic", trace);
        var scheduler = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        var runner = new EngineRunner();
        runner.RegisterModule(new BindingInstaller(owner));
        runner.Setup(new Application(Side.Server), scheduler);
        scheduler.Execute();
        runner.RunMainThreadFrameBegin();
        Assert.Equal(1, owner.Frames);
        await runner.DestroyAsync();
        Assert.Equal(["stop:dynamic", "dispose:dynamic"], trace);
    }

    [Module(ModuleScope.Simulation)]
    private sealed class BindingInstaller(Owner owner) : IModuleInstaller
    {
        public string Name => "BindingTest";
        public void OnRegisterServices(ContainerBuilder builder) => builder.RegisterInstance(owner).As<IContentBindingRuntime>();
    }

    private sealed class Composition(Owner owner) : IStaticRuntimeComposition
    {
        public void RegisterModules(IStaticModuleRegistry registry) { }
        public void RegisterServices(IStaticServiceRegistry registry) =>
            registry.Register<IContentBindingRuntime, Owner>(ModuleScope.Simulation, ServiceLifetime.Singleton, _ => owner);
        public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry) { }
    }
}
