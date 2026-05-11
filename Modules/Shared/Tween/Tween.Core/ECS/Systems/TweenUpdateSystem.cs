using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Tweening;

namespace Karpik.Engine.Shared;

public class TweenUpdateSystem : ISystemLate
{
    [DI] private Tween _tween = null!;
    [DI] private Time _time = null!;
    
    public void LateRun()
    {
        _tween.Update(_time.DeltaTime);
    }
}

public class TweenUpdatePausableSystem : ISystemLate
{
    [DI] private Tween _tween = null!;
    [DI] private Time _time = null!;
    
    public void LateRun()
    {
        if (!_time.IsPaused)
        {
            _tween.UpdatePausable(_time.DeltaTime);
        }
    }
}