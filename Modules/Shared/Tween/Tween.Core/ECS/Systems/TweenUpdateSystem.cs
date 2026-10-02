using Karpik.Engine.Core;
using Karpik.Engine.Shared.Tweening;

namespace Karpik.Engine.Shared;

public class TweenUpdateSystem(Tween tween, Time time) : ISystemLateUpdate
{
    public void LateUpdate()
    {
        tween.Update(time.DeltaTime);
    }
}

public class TweenUpdatePausableSystem(Tween tween, Time time) : ISystemLateUpdate
{
    public void LateUpdate()
    {
        if (!time.IsPaused)
        {
            tween.UpdatePausable(time.DeltaTime);
        }
    }
}