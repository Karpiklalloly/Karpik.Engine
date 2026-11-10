namespace Karpik.Engine.Shared.ECS.Scheduling;

public readonly struct EcsComponentAccessDescriptor
{
    public EcsComponentAccessDescriptor(Type componentType, EcsAccessMode mode)
        : this(EcsComponentAccessKey.ForClr(componentType), mode) { }

    public EcsComponentAccessDescriptor(EcsComponentAccessKey key, EcsAccessMode mode)
    {
        if (key.ClrType is null && key.DynamicId is null) throw new ArgumentException("Component access key is empty.", nameof(key));
        Key = key; Mode = mode;
    }

    public EcsComponentAccessKey Key { get; }
    public Type ComponentType => Key.DynamicId is not null
        ? throw new InvalidOperationException("Dynamic schema access has no CLR component type; use Key.")
        : Key.ClrType!;
    public EcsAccessMode Mode { get; }
}
