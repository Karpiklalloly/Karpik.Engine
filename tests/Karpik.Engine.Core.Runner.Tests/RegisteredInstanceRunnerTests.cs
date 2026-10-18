using System.Collections.Concurrent;
using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS.Scheduling;
using Xunit;

[Collection(nameof(EcsUpdateSchedulerRuntimeCollection))]
public sealed class RegisteredInstanceRunnerTests
{
    [Theory]
    [InlineData(false, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(false, EcsUpdateSchedulerMode.Parallel)]
    [InlineData(true, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(true, EcsUpdateSchedulerMode.Parallel)]
    public void Runner_ExecutesRepeatedTypeInstancesAndNativeSystem_InPipelineOrder(bool useStatic, EcsUpdateSchedulerMode mode)
    {
        var log = new ConcurrentQueue<string>();
        var installer = new CounterInstaller(new InstanceCounter("a", log), new InstanceCounter("b", log), log);
        var runner = new EngineRunner { UpdateSchedulerMode = mode };
        if (useStatic) runner.RegisterStaticComposition(new CounterComposition(installer, log));
        else runner.RegisterModule(installer);
        var mainThread = new MainThreadScheduler(Environment.CurrentManagedThreadId);
        try
        {
            runner.Setup(new Application(Side.Server), mainThread);
            mainThread.Execute();
            runner.RunGameplayFrame(0.001);
            runner.RunGameplayFrame(0.001);
            Assert.Equal(["native", "b", "a", "native", "b", "a"], log.ToArray());
        }
        finally { runner.Destroy(); }
    }

    [Fact]
    public void Registry_UsesReferenceIdentity_AndRejectedRegistrationDoesNotReserveIdOrInstance()
    {
        var registry = new SystemRegistry();
        var first = new InstanceCounter("a", new());
        var second = new InstanceCounter("b", new());
        var third = new InstanceCounter("c", new());
        registry.AddInstance("mod:a", first);
        Assert.Throws<InvalidOperationException>(() => registry.AddInstance("mod:b", first));
        registry.AddInstance("mod:b", second);
        Assert.Throws<InvalidOperationException>(() => registry.AddInstance("mod:a", third));
        registry.AddInstance("mod:c", third);
        Assert.True(registry.TryGetInstanceId(first, out string firstId));
        Assert.Equal("mod:a", firstId);
        Assert.True(registry.TryGetInstanceId(second, out string secondId));
        Assert.Equal("mod:b", secondId);
        Assert.False(registry.TryGetInstanceId(new InstanceCounter("unknown", new()), out _));
    }

    [Fact]
    public void BindUpdateDescriptors_PreservesMetadata_AndFollowsActualInstanceOrder()
    {
        var first = new InstanceCounter("a", new());
        var second = new InstanceCounter("b", new());
        var native = new NativeCounter(new());
        var registry = new SystemRegistry();
        registry.AddInstance("mod:a", first);
        registry.AddInstance("mod:b", second);
        EcsUpdateSystemDescriptor[] metadata = [
            new(typeof(InstanceCounter), true,
                new EcsComponentAccessDescriptor[] { new(typeof(int), EcsAccessMode.Read) },
                new EcsSystemOrderDescriptor[] { new(typeof(NativeCounter), EcsOrderKind.After) }),
            new(typeof(NativeCounter), false, default, default)];
        EcsUpdateSystemDescriptor[] bound = EngineRunner.BindUpdateDescriptors([second, native, first], metadata, registry);
        Assert.Equal("mod:b", bound[0].SystemId);
        Assert.Equal($"engine:{typeof(NativeCounter).FullName}", bound[1].SystemId);
        Assert.Equal("mod:a", bound[2].SystemId);
        Assert.True(bound[0].IsSequential);
        Assert.Equal(metadata[0].Accesses.ToArray(), bound[0].Accesses.ToArray());
        Assert.Equal(metadata[0].Orders.ToArray(), bound[0].Orders.ToArray());
        Assert.Null(metadata[0].SystemId);
    }

    [Fact]
    public void BindUpdateDescriptors_MissingOrDuplicateMetadata_Throws()
    {
        var instance = new InstanceCounter("a", new());
        var registry = new SystemRegistry();
        registry.AddInstance("mod:a", instance);
        Assert.Throws<EcsUpdateGraphBuildException>(() => EngineRunner.BindUpdateDescriptors([instance], [], registry));
        EcsUpdateSystemDescriptor descriptor = new(typeof(InstanceCounter), true, default, default);
        Assert.Throws<EcsUpdateGraphBuildException>(() => EngineRunner.BindUpdateDescriptors([instance], [descriptor, descriptor], registry));
    }

    [Module(ModuleScope.Simulation)]
    private sealed class CounterInstaller(InstanceCounter first, InstanceCounter second, ConcurrentQueue<string> log) : IModuleInstaller
    {
        public string Name => nameof(CounterInstaller);
        public IModule CreateModule() => new CounterModule(first, second);
        public void OnRegisterServices(ContainerBuilder builder) => builder.RegisterInstance(log).AsSelf();
    }

    private sealed class CounterModule(InstanceCounter first, InstanceCounter second) : IModule
    {
        public void Add(ISystemRegistry systems)
        {
            systems.AddInstance("mod:a", first, order: 20);
            systems.AddInstance("mod:b", second, order: 10);
            systems.Add<NativeCounter>();
        }
    }

    private sealed class CounterComposition(CounterInstaller installer, ConcurrentQueue<string> log) : IStaticRuntimeComposition
    {
        public void RegisterModules(IStaticModuleRegistry registry) => registry.Add(installer);
        public void RegisterServices(IStaticServiceRegistry registry) =>
            registry.Register<NativeCounter, NativeCounter>(ModuleScope.Simulation, ServiceLifetime.Singleton, _ => new NativeCounter(log));
        public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry) =>
            registry.AddUpdate(new Karpik.Engine.Shared.ECS.Scheduling.Generated.GeneratedEcsUpdateRegistryProvider());
    }

    [SequentialSystem]
    internal sealed class InstanceCounter(string name, ConcurrentQueue<string> log) : ISystemUpdate
    {
        public void Update() => log.Enqueue(name);
        public override bool Equals(object? obj) => obj is InstanceCounter;
        public override int GetHashCode() => 0;
    }

    [SequentialSystem]
    internal sealed class NativeCounter(ConcurrentQueue<string> log) : ISystemUpdate
    {
        public void Update() => log.Enqueue("native");
    }
}
