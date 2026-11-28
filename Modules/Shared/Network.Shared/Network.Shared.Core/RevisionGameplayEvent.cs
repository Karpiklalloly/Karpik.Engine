using DCFApixels.DragonECS;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Shared.Network.Core;

/// <summary>Transport revision carried on the deferred ECS event, not in the RPC payload.</summary>
public struct RevisionGameplayStamp : IEcsComponent
{
    public long Revision;
}

public static class RevisionGameplayEvent
{
    public static void Enqueue<T>(EcsEventWorld world, EcsPool<RevisionGameplayStamp> stamps, T value, long revision)
        where T : struct, IEcsComponentEvent
    {
        if (revision == 0)
        {
            world.SendEvent(value);
            return;
        }

        int entity = world.NewEntity();
        world.GetPool<T>().Add(entity) = value;
        stamps.Add(entity).Revision = revision;
    }

    /// <summary>Call at gameplay consumption, not only when decoding. Pass a cached pool.</summary>
    /// <param name="allowUnversioned">Only for a legacy gameplay session, never for old deferred work in a live transition.</param>
    public static bool IsCurrent(EcsPool<RevisionGameplayStamp> stamps, int eventEntity, long activeRevision, bool allowUnversioned = false) =>
        stamps.Has(eventEntity) ? stamps.Get(eventEntity).Revision == activeRevision : activeRevision == 0 || allowUnversioned;
}
