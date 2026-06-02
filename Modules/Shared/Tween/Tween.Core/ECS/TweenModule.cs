using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Tweening;

internal class TweenModule : IModule
{
    public void Import(IBuilder b)
    {
        b.Add(new TweenUpdateSystem(), EcsConsts.POST_END_LAYER);
        b.Add(new TweenUpdatePausableSystem(), EcsConsts.POST_END_LAYER);
    }
}