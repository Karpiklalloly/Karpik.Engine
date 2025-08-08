namespace Karpik.Engine.Shared;

public class TweenUpdateSystem : IEcsRun
{
    public void Run()
    {
        Tween.Instance.Update(Time.DeltaTime);
    }
}

public class TweenUpdatePausableSystem : IEcsRun
{
    public void Run()
    {
        if (!Time.IsPaused)
        {
            Tween.Instance.UpdatePausable(Time.DeltaTime);
        }
    }
}