namespace Karpik.Engine.Shared.ECS.Scheduling;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public abstract class EcsOrderAttribute : Attribute
{
    private protected EcsOrderAttribute(Type systemType, EcsOrderKind kind)
    {
        SystemType = systemType;
        Kind = kind;
    }

    public Type SystemType { get; }
    public EcsOrderKind Kind { get; }
}
