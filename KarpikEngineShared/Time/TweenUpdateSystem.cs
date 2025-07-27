namespace Karpik.Engine.Shared;

public class TweenUpdateSystem : IEcsRun
{
    public void Run()
    {
        Tween.Update(Time.DeltaTime);
    }
}

public class TweenUpdatePausableSystem : IEcsRun
{
    public void Run()
    {
        if (!Time.IsPaused)
        {
            Tween.UpdatePausable(Time.DeltaTime);
        }
    }
}