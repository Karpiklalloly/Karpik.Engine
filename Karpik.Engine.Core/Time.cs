namespace Karpik.Engine.Core;

public static class Time
{
    public static double DeltaTime { get; private set; }
    public static double TotalTime { get; private set; }

    public static bool IsPaused
    {
        get => _isPaused;
        set => _isPaused = value;
    }
    
    private static bool _isPaused;

    internal static void Update(double deltaTime)
    {
        DeltaTime = deltaTime;
        if (!IsPaused) TotalTime += deltaTime;
    }
}