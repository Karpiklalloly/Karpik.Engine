namespace Karpik.Engine.Shared.ECS.Scheduling;

public readonly struct EcsSystemOrderDescriptor
{
    public EcsSystemOrderDescriptor(Type targetSystemType, EcsOrderKind kind)
    {
        TargetSystemType = targetSystemType;
        Kind = kind;
    }

    public Type TargetSystemType { get; }
    public EcsOrderKind Kind { get; }
}
