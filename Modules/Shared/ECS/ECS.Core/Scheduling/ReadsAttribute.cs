namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class ReadsAttribute<TComponent> : EcsAccessAttribute
    where TComponent : struct, IEcsComponent
{
    public ReadsAttribute()
        : base(typeof(TComponent), EcsAccessMode.Read)
    {
    }
}
