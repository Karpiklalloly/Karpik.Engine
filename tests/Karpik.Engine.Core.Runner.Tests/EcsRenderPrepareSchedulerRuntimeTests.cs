using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using DragonExtensions;
using Xunit;

public sealed class EcsRenderPrepareSchedulerRuntimeTests
{
    [Fact]
    public void DeterministicMode_InvokesRenderPrepareSystems()
    {
        var system = new CountingRenderPrepareSystem();
        using var scheduler = new EcsRenderPrepareScheduler();
        scheduler.Initialize(
            [system],
            [new EcsUpdateSystemDescriptor(
                typeof(CountingRenderPrepareSystem),
                false,
                Array.Empty<EcsComponentAccessDescriptor>(),
                Array.Empty<EcsSystemOrderDescriptor>())],
            EcsUpdateSchedulerMode.Deterministic);

        scheduler.RenderPrepare();

        Assert.Equal(1, system.Count);
    }

    [Fact]
    public void LifecycleBridge_InvokesRenderPrepareSystem()
    {
        var system = new CountingRenderPrepareSystem();
        var bridge = new RenderPrepareSystem(system);

        bridge.RenderPrepare();

        Assert.Equal(1, system.Count);
        Assert.Same(system, bridge.System);
    }

    [Fact]
    public void BuilderContract_ExposesRenderPrepareRegistration()
    {
        var method = typeof(IBuilder).GetMethod(
            "Add",
            [typeof(ISystemRenderPrepare), typeof(string), typeof(int)]);

        Assert.NotNull(method);
    }

    internal sealed class CountingRenderPrepareSystem : ISystemRenderPrepare
    {
        public int Count;

        public void RenderPrepare()
        {
            Count++;
        }
    }
}
