namespace Karpik.Engine.Shared.ECS.Scheduling;

public readonly struct EcsSystemOrderDescriptor
{
    public EcsSystemOrderDescriptor(Type targetSystemType, EcsOrderKind kind)
    {
        ArgumentNullException.ThrowIfNull(targetSystemType);
        TargetSystemType = targetSystemType;
        TargetSystemId = null;
        Kind = kind;
    }

    public EcsSystemOrderDescriptor(string targetSystemId, EcsOrderKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSystemId);
        TargetSystemType = null;
        TargetSystemId = targetSystemId;
        Kind = kind;
    }

    public Type? TargetSystemType { get; }
    public string? TargetSystemId { get; }
    public EcsOrderKind Kind { get; }
}
