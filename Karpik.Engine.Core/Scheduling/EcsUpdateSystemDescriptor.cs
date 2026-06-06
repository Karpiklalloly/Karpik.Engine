namespace Karpik.Engine.Shared.ECS.Scheduling;

public readonly struct EcsUpdateSystemDescriptor
{
    public EcsUpdateSystemDescriptor(
        Type systemType,
        bool IsSequential,
        ReadOnlyMemory<EcsComponentAccessDescriptor> accesses,
        ReadOnlyMemory<EcsSystemOrderDescriptor> orders)
    {
        SystemType = systemType;
        this.IsSequential = IsSequential;
        Accesses = accesses;
        Orders = orders;
    }

    public Type SystemType { get; }
    public bool IsSequential { get; }
    public ReadOnlyMemory<EcsComponentAccessDescriptor> Accesses { get; }
    public ReadOnlyMemory<EcsSystemOrderDescriptor> Orders { get; }
}
