namespace Karpik.Engine.Shared.ECS.Scheduling;

public readonly struct EcsComponentAccessDescriptor
{
    public EcsComponentAccessDescriptor(Type componentType, EcsAccessMode mode)
    {
        ComponentType = componentType;
        Mode = mode;
    }

    public Type ComponentType { get; }
    public EcsAccessMode Mode { get; }
}
