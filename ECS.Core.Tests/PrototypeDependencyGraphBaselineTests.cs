using DCFApixels.DragonECS;
using DragonExtensions;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.ECS;
using Xunit;

public sealed class PrototypeDependencyGraphBaselineTests
{
    [Fact]
    public void DependencyGraph_ReadThenRead_DoesNotCreateDependency()
    {
        SystemExecutionNode[] nodes = CreateGraph(new ReadBaselineA(), new ReadBaselineA());

        AssertDependencies(nodes[0]);
        AssertDependencies(nodes[1]);
    }

    [Fact]
    public void DependencyGraph_MixedReadWriteAspect_ConflictsWithReadersAndWriters()
    {
        SystemExecutionNode[] nodes = CreateGraph(
            new ReadBaselineA(),
            new ReadBaselineB(),
            new ReadAWriteB(),
            new WriteBaselineA());

        AssertDependencies(nodes[0]);
        AssertDependencies(nodes[1]);
        AssertDependencies(nodes[2], nodes[1]);
        AssertDependencies(nodes[3], nodes[0], nodes[2]);
    }

    [Fact]
    public void DependencyGraph_UnknownAccess_BarriersAllLaterSystemsInRegistrationOrder()
    {
        SystemExecutionNode[] nodes = CreateGraph(
            new WriteBaselineA(),
            new UnknownBaselineAccess(),
            new ReadBaselineB(),
            new WriteBaselineC());

        AssertDependencies(nodes[0]);
        AssertDependencies(nodes[1], nodes[0]);
        AssertDependencies(nodes[2], nodes[1]);
        AssertDependencies(nodes[3], nodes[1]);
    }

    [Fact]
    public void DependencyGraph_NodeDestroyClearsPrototypeReflectionState()
    {
        SystemExecutionNode[] nodes = CreateGraph(new WriteBaselineA(), new ReadBaselineA());

        Assert.NotEmpty(nodes[0].WriteTypes);
        Assert.NotEmpty(nodes[1].ReadTypes);
        Assert.NotEmpty(nodes[1].Dependencies);

        nodes[0].Destroy();
        nodes[1].Destroy();

        Assert.Empty(nodes[0].WriteTypes);
        Assert.Empty(nodes[0].ReadTypes);
        Assert.Empty(nodes[0].Dependencies);
        Assert.Empty(nodes[1].WriteTypes);
        Assert.Empty(nodes[1].ReadTypes);
        Assert.Empty(nodes[1].Dependencies);
    }

    private static SystemExecutionNode[] CreateGraph(params IEcsRunParallel[] systems)
    {
        return EcsRunParallelRunner.CreateDependencyGraph(systems);
    }

    private static void AssertDependencies(SystemExecutionNode node, params SystemExecutionNode[] expected)
    {
        Assert.Equal(expected.Length, node.Dependencies.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Same(expected[i], node.Dependencies[i]);
        }
    }
}

internal readonly struct BaselineComponentA : IEcsComponent;
internal readonly struct BaselineComponentB : IEcsComponent;
internal readonly struct BaselineComponentC : IEcsComponent;

internal sealed class ReadBaselineA : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsReadonlyPool<BaselineComponentA> Component = default;
    }

    public void RunParallel()
    {
    }
}

internal sealed class ReadBaselineB : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsReadonlyPool<BaselineComponentB> Component = default;
    }

    public void RunParallel()
    {
    }
}

internal sealed class WriteBaselineA : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsPool<BaselineComponentA> Component = null!;
    }

    public void RunParallel()
    {
    }
}

internal sealed class WriteBaselineC : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsPool<BaselineComponentC> Component = null!;
    }

    public void RunParallel()
    {
    }
}

internal sealed class ReadAWriteB : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsReadonlyPool<BaselineComponentA> Read = default;
        public EcsPool<BaselineComponentB> Write = null!;
    }

    public void RunParallel()
    {
    }
}

internal sealed class UnknownBaselineAccess : IEcsRunParallel
{
    public void RunParallel()
    {
    }
}
