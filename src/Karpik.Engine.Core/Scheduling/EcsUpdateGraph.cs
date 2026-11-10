namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class EcsUpdateGraph
{
    public static readonly EcsUpdateGraph Empty = new(
        Array.Empty<Type>(),
        Array.Empty<EcsComponentAccessKey>(),
        Array.Empty<EcsUpdateGraphNode>(),
        Array.Empty<int>());

    internal EcsUpdateGraph(
        Type[] systemTypes,
        EcsComponentAccessKey[] accessKeys,
        EcsUpdateGraphNode[] nodes,
        int[] executionOrder)
    {
        SystemTypes = systemTypes;
        AccessKeys = accessKeys;
        ComponentTypes = accessKeys.Where(key => key.ClrType is not null).Select(key => key.ClrType!).ToArray();
        Nodes = nodes;
        ExecutionOrder = executionOrder;
    }

    public ReadOnlyMemory<Type> SystemTypes { get; }
    public ReadOnlyMemory<Type> ComponentTypes { get; }
    public ReadOnlyMemory<EcsComponentAccessKey> AccessKeys { get; }
    public ReadOnlyMemory<EcsUpdateGraphNode> Nodes { get; }
    public ReadOnlyMemory<int> ExecutionOrder { get; }
}
