namespace Karpik.Engine.Shared.ECS.Scheduling;

public readonly struct EcsUpdateSystemDescriptor(
    Type systemType,
    bool IsSequential,
    ReadOnlyMemory<EcsComponentAccessDescriptor> accesses,
    ReadOnlyMemory<EcsSystemOrderDescriptor> orders)
{
    public EcsUpdateSystemDescriptor(
        Type systemType,
        bool IsSequential,
        ReadOnlyMemory<EcsComponentAccessDescriptor> accesses,
        ReadOnlyMemory<EcsSystemOrderDescriptor> orders,
        string systemId)
        : this(systemType, IsSequential, accesses, orders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        SystemId = systemId;
    }

    public Type SystemType { get; } = systemType;
    public bool IsSequential { get; } = IsSequential;
    public ReadOnlyMemory<EcsComponentAccessDescriptor> Accesses { get; } = accesses;
    public ReadOnlyMemory<EcsSystemOrderDescriptor> Orders { get; } = orders;
    public string? SystemId { get; } = null;
}
