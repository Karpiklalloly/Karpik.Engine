namespace Karpik.Engine.Core;

public class Time
{
    public double DeltaTime { get; private set; }
    public double FixedDeltaTime { get; internal set; } = Application.TICK_DT;
    public double TotalTime { get; private set; }

    public bool IsPaused { get; set; }

    internal void Update(double deltaTime)
    {
        DeltaTime = deltaTime;
        if (!IsPaused) TotalTime += deltaTime;
    }
}