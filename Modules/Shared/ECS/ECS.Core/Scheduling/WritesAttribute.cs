namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class WritesAttribute<TComponent> : EcsAccessAttribute
    where TComponent : struct, IEcsComponent
{
    public WritesAttribute()
        : base(typeof(TComponent), EcsAccessMode.Write)
    {
    }
}
