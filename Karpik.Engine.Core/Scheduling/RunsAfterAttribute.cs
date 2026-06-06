using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class RunsAfterAttribute<TSystem> : EcsOrderAttribute
    where TSystem : ISystemUpdate
{
    public RunsAfterAttribute()
        : base(typeof(TSystem), EcsOrderKind.After)
    {
    }
}
