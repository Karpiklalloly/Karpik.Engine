using System.Composition;
using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS.Scheduling;
using Xunit;

public sealed class RuntimeSystemRegistrationTests
{
    [Fact]
    public void RuntimeSystem_IsNotAutomaticallyRegisteredAsAnAttributedService()
    {
        var builder = new ContainerBuilder();
        AttributedServiceRegistrar.Register(builder, [typeof(RuntimeAdapter)], ModuleScope.Simulation);
        using var container = builder.Build();
        Assert.False(container.IsRegistered<RuntimeAdapter>());
    }

    [RuntimeSystem, Export, ServiceRegistration(ModuleScope.Simulation)]
    public sealed class RuntimeAdapter : ISystemUpdate
    {
        public void Update() { }
    }
}
