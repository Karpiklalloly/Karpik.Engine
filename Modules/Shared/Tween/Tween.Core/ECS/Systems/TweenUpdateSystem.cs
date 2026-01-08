using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Tweening;

namespace Karpik.Engine.Shared;

public class TweenUpdateSystem : IEcsRun
{
    [DI] private Tween _tween = null!;
    
    public void Run()
    {
        _tween.Update(Time.DeltaTime);
    }
}

public class TweenUpdatePausableSystem : IEcsRun
{
    [DI] private Tween _tween = null!;
    
    public void Run()
    {
        if (!Time.IsPaused)
        {
            _tween.UpdatePausable(Time.DeltaTime);
        }
    }
}