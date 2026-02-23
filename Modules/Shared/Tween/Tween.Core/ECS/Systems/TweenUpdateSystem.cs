using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Tweening;

namespace Karpik.Engine.Shared;

public class TweenUpdateSystem : IEcsRun
{
    [DI] private Tween _tween = null!;
    [DI] private Time _time = null!;
    
    public void Run()
    {
        _tween.Update(_time.DeltaTime);
    }
}

public class TweenUpdatePausableSystem : IEcsRun
{
    [DI] private Tween _tween = null!;
    [DI] private Time _time = null!;
    
    public void Run()
    {
        if (!_time.IsPaused)
        {
            _tween.UpdatePausable(_time.DeltaTime);
        }
    }
}