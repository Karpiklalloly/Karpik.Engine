namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class ReadsAttribute<TComponent> : EcsAccessAttribute
{
    public ReadsAttribute()
        : base(typeof(TComponent), EcsAccessMode.Read)
    {
    }
}
