using DCFApixels.DragonECS;

namespace Karpik.Game.Modules;

public class AIModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new UpdateFollowTargetSystem())
            .Add(new FollowTargetSystem());
    }
}