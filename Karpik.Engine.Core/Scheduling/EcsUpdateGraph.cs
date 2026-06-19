namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class EcsUpdateGraph
{
    public static readonly EcsUpdateGraph Empty = new(
        Array.Empty<Type>(),
        Array.Empty<Type>(),
        Array.Empty<EcsUpdateGraphNode>(),
        Array.Empty<int>());

    internal EcsUpdateGraph(
        Type[] systemTypes,
        Type[] componentTypes,
        EcsUpdateGraphNode[] nodes,
        int[] executionOrder)
    {
        SystemTypes = systemTypes;
        ComponentTypes = componentTypes;
        Nodes = nodes;
        ExecutionOrder = executionOrder;
    }

    public ReadOnlyMemory<Type> SystemTypes { get; }
    public ReadOnlyMemory<Type> ComponentTypes { get; }
    public ReadOnlyMemory<EcsUpdateGraphNode> Nodes { get; }
    public ReadOnlyMemory<int> ExecutionOrder { get; }
}
