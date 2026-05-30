using DCFApixels.DragonECS;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.ECS;

var tests = new (string Name, Action Run)[]
{
    ("DependencyGraph_Empty_ReturnsEmptyGraph", DependencyGraph_Empty_ReturnsEmptyGraph),
    ("DependencyGraph_DisjointWrites_DoNotCreateDependency", DependencyGraph_DisjointWrites_DoNotCreateDependency),
    ("DependencyGraph_WriteThenRead_CreatesDependency", DependencyGraph_WriteThenRead_CreatesDependency),
    ("DependencyGraph_ReadThenWrite_CreatesDependency", DependencyGraph_ReadThenWrite_CreatesDependency),
    ("DependencyGraph_WriteThenWrite_CreatesDependency", DependencyGraph_WriteThenWrite_CreatesDependency),
    ("DependencyGraph_UnknownAccess_CreatesSequentialBarrier", DependencyGraph_UnknownAccess_CreatesSequentialBarrier),
    ("DependencyGraph_Conflicts_PreserveRegistrationOrder", DependencyGraph_Conflicts_PreserveRegistrationOrder)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static void DependencyGraph_Empty_ReturnsEmptyGraph()
{
    SystemExecutionNode[] nodes = CreateGraph();

    if (nodes.Length != 0)
    {
        throw new InvalidOperationException($"Expected empty graph, got {nodes.Length} nodes.");
    }
}

static void DependencyGraph_DisjointWrites_DoNotCreateDependency()
{
    SystemExecutionNode[] nodes = CreateGraph(new WriteA(), new WriteB());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1]);
}

static void DependencyGraph_WriteThenRead_CreatesDependency()
{
    SystemExecutionNode[] nodes = CreateGraph(new WriteA(), new ReadA());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1], nodes[0]);
}

static void DependencyGraph_ReadThenWrite_CreatesDependency()
{
    SystemExecutionNode[] nodes = CreateGraph(new ReadA(), new WriteA());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1], nodes[0]);
}

static void DependencyGraph_WriteThenWrite_CreatesDependency()
{
    SystemExecutionNode[] nodes = CreateGraph(new WriteA(), new WriteA());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1], nodes[0]);
}

static void DependencyGraph_UnknownAccess_CreatesSequentialBarrier()
{
    SystemExecutionNode[] nodes = CreateGraph(new WriteA(), new UnknownAccess(), new WriteB());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1], nodes[0]);
    AssertDependencies(nodes[2], nodes[1]);
}

static void DependencyGraph_Conflicts_PreserveRegistrationOrder()
{
    SystemExecutionNode[] nodes = CreateGraph(new WriteA(), new ReadA(), new WriteA());

    AssertDependencies(nodes[0]);
    AssertDependencies(nodes[1], nodes[0]);
    AssertDependencies(nodes[2], nodes[0], nodes[1]);
}

static SystemExecutionNode[] CreateGraph(params IEcsRunParallel[] systems)
{
    return EcsRunParallelRunner.CreateDependencyGraph(systems);
}

static void AssertDependencies(SystemExecutionNode node, params SystemExecutionNode[] expected)
{
    if (node.Dependencies.Count != expected.Length)
    {
        throw new InvalidOperationException(
            $"Expected {expected.Length} dependencies for node {node.Index}, got {node.Dependencies.Count}.");
    }

    for (int i = 0; i < expected.Length; i++)
    {
        if (!ReferenceEquals(expected[i], node.Dependencies[i]))
        {
            throw new InvalidOperationException(
                $"Dependency {i} mismatch for node {node.Index}. Expected node {expected[i].Index}, got node {node.Dependencies[i].Index}.");
        }
    }
}

readonly struct ComponentA : IEcsComponent;
readonly struct ComponentB : IEcsComponent;

sealed class WriteA : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsPool<ComponentA> Component = null!;
    }

    public void RunParallel()
    {
    }
}

sealed class WriteB : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsPool<ComponentB> Component = null!;
    }

    public void RunParallel()
    {
    }
}

sealed class ReadA : IEcsRunParallel
{
    private sealed class Aspect : EcsAspect
    {
        public EcsReadonlyPool<ComponentA> Component = default;
    }

    public void RunParallel()
    {
    }
}

sealed class UnknownAccess : IEcsRunParallel
{
    public void RunParallel()
    {
    }
}
