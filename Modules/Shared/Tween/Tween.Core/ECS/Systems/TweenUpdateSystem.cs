using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Tweening;

namespace Karpik.Engine.Shared;

public class TweenUpdateSystem : ISystemLateUpdate
{
    [DI] private Tween _tween = null!;
    [DI] private Time _time = null!;
    
    public void LateUpdate()
    {
        _tween.Update(_time.DeltaTime);
    }
}

public class TweenUpdatePausableSystem : ISystemLateUpdate
{
    [DI] private Tween _tween = null!;
    [DI] private Time _time = null!;
    
    public void LateUpdate()
    {
        if (!_time.IsPaused)
        {
            _tween.UpdatePausable(_time.DeltaTime);
        }
    }
}