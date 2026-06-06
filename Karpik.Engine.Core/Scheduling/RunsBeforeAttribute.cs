using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class RunsBeforeAttribute<TSystem> : EcsOrderAttribute
    where TSystem : ISystemUpdate
{
    public RunsBeforeAttribute()
        : base(typeof(TSystem), EcsOrderKind.Before)
    {
    }
}
