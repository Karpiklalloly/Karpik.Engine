namespace Karpik.Engine.Shared.ECS.Scheduling;

public readonly struct EcsUpdateGraphNode
{
    internal EcsUpdateGraphNode(
        int systemId,
        bool isSequential,
        ReadOnlyMemory<int> readComponentIds,
        ReadOnlyMemory<int> writeComponentIds,
        ReadOnlyMemory<int> dependencySystemIds)
    {
        SystemId = systemId;
        IsSequential = isSequential;
        ReadComponentIds = readComponentIds;
        WriteComponentIds = writeComponentIds;
        DependencySystemIds = dependencySystemIds;
    }

    public int SystemId { get; }
    public bool IsSequential { get; }
    public ReadOnlyMemory<int> ReadComponentIds { get; }
    public ReadOnlyMemory<int> WriteComponentIds { get; }
    public ReadOnlyMemory<int> DependencySystemIds { get; }
}
