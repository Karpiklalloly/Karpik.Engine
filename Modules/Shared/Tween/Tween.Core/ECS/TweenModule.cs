using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Tweening;

internal class TweenModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<TweenUpdateSystem>(EcsConsts.POST_END_LAYER);
        systems.Add<TweenUpdatePausableSystem>(EcsConsts.POST_END_LAYER);
    }
}