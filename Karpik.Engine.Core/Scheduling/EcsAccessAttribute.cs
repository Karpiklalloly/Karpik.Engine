namespace Karpik.Engine.Shared.ECS.Scheduling;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public abstract class EcsAccessAttribute : Attribute
{
    private protected EcsAccessAttribute(Type componentType, EcsAccessMode mode)
    {
        ComponentType = componentType;
        Mode = mode;
    }

    public Type ComponentType { get; }
    public EcsAccessMode Mode { get; }
}
