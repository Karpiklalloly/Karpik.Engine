using Karpik.Engine.Shared.ECS.Scheduling;
using Xunit;

public sealed class EcsUpdateGraphBuilderTests
{
    [Fact]
    public void Build_EmptyRegistry_ReturnsEmptyGraph()
    {
        EcsUpdateGraph graph = EcsUpdateGraphBuilder.Build(
            Array.Empty<Type>(),
            Array.Empty<EcsUpdateSystemDescriptor>());

        Assert.Empty(graph.Nodes.ToArray());
        Assert.Empty(graph.ExecutionOrder.ToArray());
        Assert.Empty(graph.ComponentTypes.ToArray());
        Assert.Empty(graph.SystemTypes.ToArray());
    }

    [Fact]
    public void Build_DisjointAccess_DoesNotCreateDependency()
    {
        EcsUpdateGraph graph = Build(
            Registered<GraphWriteA, GraphWriteB>(),
            Descriptor<GraphWriteA>(Write<GraphComponentA>()),
            Descriptor<GraphWriteB>(Write<GraphComponentB>()));

        AssertDependencies(graph, systemId: 0);
        AssertDependencies(graph, systemId: 1);
        AssertExecutionOrder(graph, 0, 1);
    }

    [Fact]
    public void Build_WriteThenRead_CreatesDependency()
    {
        EcsUpdateGraph graph = Build(
            Registered<GraphWriteA, GraphReadA>(),
            Descriptor<GraphWriteA>(Write<GraphComponentA>()),
            Descriptor<GraphReadA>(Read<GraphComponentA>()));

        AssertDependencies(graph, systemId: 0);
        AssertDependencies(graph, systemId: 1, 0);
        AssertExecutionOrder(graph, 0, 1);
    }

    [Fact]
    public void Build_ReadThenRead_DoesNotCreateDependency()
    {
        EcsUpdateGraph graph = Build(
            Registered<GraphReadA, GraphReadA2>(),
            Descriptor<GraphReadA>(Read<GraphComponentA>()),
            Descriptor<GraphReadA2>(Read<GraphComponentA>()));

        AssertDependencies(graph, systemId: 0);
        AssertDependencies(graph, systemId: 1);
        AssertExecutionOrder(graph, 0, 1);
    }

    [Fact]
    public void Build_SequentialSystem_BarriersLaterDisjointSystems()
    {
        EcsUpdateGraph graph = Build(
            Registered<GraphWriteA, GraphSequential, GraphReadB, GraphWriteC>(),
            Descriptor<GraphWriteA>(Write<GraphComponentA>()),
            Descriptor<GraphSequential>(isSequential: true),
            Descriptor<GraphReadB>(Read<GraphComponentB>()),
            Descriptor<GraphWriteC>(Write<GraphComponentC>()));

        AssertDependencies(graph, systemId: 0);
        AssertDependencies(graph, systemId: 1, 0);
        AssertDependencies(graph, systemId: 2, 1);
        AssertDependencies(graph, systemId: 3, 1);
        AssertExecutionOrder(graph, 0, 1, 2, 3);
    }

    [Fact]
    public void Build_RunsAfterEarlierRegisteredTarget_ReordersExecution()
    {
        EcsUpdateGraph graph = Build(
            Registered<GraphWriteA, GraphReadA>(),
            Descriptor<GraphWriteA>(After<GraphReadA>()),
            Descriptor<GraphReadA>());

        AssertDependencies(graph, systemId: 0, 1);
        AssertDependencies(graph, systemId: 1);
        AssertExecutionOrder(graph, 1, 0);
    }

    [Fact]
    public void Build_RunsBeforeLaterRegisteredTarget_CreatesTargetDependency()
    {
        EcsUpdateGraph graph = Build(
            Registered<GraphWriteA, GraphReadA>(),
            Descriptor<GraphWriteA>(Before<GraphReadA>()),
            Descriptor<GraphReadA>());

        AssertDependencies(graph, systemId: 0);
        AssertDependencies(graph, systemId: 1, 0);
        AssertExecutionOrder(graph, 0, 1);
    }

    [Fact]
    public void Build_DuplicateReadWriteAccess_WriteWins()
    {
        EcsUpdateGraph graph = Build(
            Registered<GraphWriteA, GraphReadA>(),
            Descriptor<GraphWriteA>(Read<GraphComponentA>(), Write<GraphComponentA>()),
            Descriptor<GraphReadA>(Read<GraphComponentA>()));

        EcsUpdateGraphNode node = graph.Nodes.Span[0];

        Assert.Empty(node.ReadComponentIds.ToArray());
        Assert.Single(node.WriteComponentIds.ToArray());
        AssertDependencies(graph, systemId: 1, 0);
    }

    [Fact]
    public void Build_MissingRegisteredSystem_Throws()
    {
        var exception = Assert.Throws<EcsUpdateGraphBuildException>(() => Build(
            Registered<GraphWriteA, GraphReadA>(),
            Descriptor<GraphWriteA>()));

        Assert.Contains(nameof(GraphReadA), exception.Message);
    }

    [Fact]
    public void Build_OrderTargetOutsideRegistration_Throws()
    {
        var exception = Assert.Throws<EcsUpdateGraphBuildException>(() => Build(
            Registered<GraphWriteA>(),
            Descriptor<GraphWriteA>(After<GraphReadA>())));

        Assert.Contains(nameof(GraphReadA), exception.Message);
    }

    [Fact]
    public void Build_ExplicitOrderCycle_Throws()
    {
        var exception = Assert.Throws<EcsUpdateGraphBuildException>(() => Build(
            Registered<GraphWriteA, GraphReadA>(),
            Descriptor<GraphWriteA>(After<GraphReadA>()),
            Descriptor<GraphReadA>(After<GraphWriteA>())));

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static EcsUpdateGraph Build(Type[] registeredSystems, params EcsUpdateSystemDescriptor[] descriptors)
    {
        return EcsUpdateGraphBuilder.Build(registeredSystems, descriptors);
    }

    private static Type[] Registered<T0, T1>()
    {
        return new[] { typeof(T0), typeof(T1) };
    }

    private static Type[] Registered<T0>()
    {
        return new[] { typeof(T0) };
    }

    private static Type[] Registered<T0, T1, T2, T3>()
    {
        return new[] { typeof(T0), typeof(T1), typeof(T2), typeof(T3) };
    }

    private static EcsUpdateSystemDescriptor Descriptor<TSystem>(
        params EcsComponentAccessDescriptor[] accesses)
    {
        return new EcsUpdateSystemDescriptor(
            typeof(TSystem),
            false,
            accesses,
            Array.Empty<EcsSystemOrderDescriptor>());
    }

    private static EcsUpdateSystemDescriptor Descriptor<TSystem>(
        params EcsSystemOrderDescriptor[] orders)
    {
        return new EcsUpdateSystemDescriptor(
            typeof(TSystem),
            false,
            Array.Empty<EcsComponentAccessDescriptor>(),
            orders);
    }

    private static EcsUpdateSystemDescriptor Descriptor<TSystem>()
    {
        return new EcsUpdateSystemDescriptor(
            typeof(TSystem),
            false,
            Array.Empty<EcsComponentAccessDescriptor>(),
            Array.Empty<EcsSystemOrderDescriptor>());
    }

    private static EcsUpdateSystemDescriptor Descriptor<TSystem>(bool isSequential)
    {
        return new EcsUpdateSystemDescriptor(
            typeof(TSystem),
            isSequential,
            Array.Empty<EcsComponentAccessDescriptor>(),
            Array.Empty<EcsSystemOrderDescriptor>());
    }

    private static EcsComponentAccessDescriptor Read<TComponent>()
    {
        return new EcsComponentAccessDescriptor(typeof(TComponent), EcsAccessMode.Read);
    }

    private static EcsComponentAccessDescriptor Write<TComponent>()
    {
        return new EcsComponentAccessDescriptor(typeof(TComponent), EcsAccessMode.Write);
    }

    private static EcsSystemOrderDescriptor After<TSystem>()
    {
        return new EcsSystemOrderDescriptor(typeof(TSystem), EcsOrderKind.After);
    }

    private static EcsSystemOrderDescriptor Before<TSystem>()
    {
        return new EcsSystemOrderDescriptor(typeof(TSystem), EcsOrderKind.Before);
    }

    private static void AssertDependencies(EcsUpdateGraph graph, int systemId, params int[] expected)
    {
        Assert.Equal(expected, graph.Nodes.Span[systemId].DependencySystemIds.ToArray());
    }

    private static void AssertExecutionOrder(EcsUpdateGraph graph, params int[] expected)
    {
        Assert.Equal(expected, graph.ExecutionOrder.ToArray());
    }
}

internal sealed class GraphWriteA;

internal sealed class GraphWriteB;

internal sealed class GraphWriteC;

internal sealed class GraphReadA;

internal sealed class GraphReadA2;

internal sealed class GraphReadB;

internal sealed class GraphSequential;

internal readonly struct GraphComponentA;

internal readonly struct GraphComponentB;

internal readonly struct GraphComponentC;
