namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class WritesAttribute<TComponent> : EcsAccessAttribute
{
    public WritesAttribute()
        : base(typeof(TComponent), EcsAccessMode.Write)
    {
    }
}
