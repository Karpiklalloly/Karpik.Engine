namespace Karpik.Engine.Shared;

public static class Time
{
    public static double DeltaTime { get; private set; }
    public static double FixedDeltaTime { get; set; } = 1.0 / 50;
    public static double TotalTime { get; private set; }

    public static bool IsPaused
    {
        get => _isPaused;
        set => _isPaused = value;
    }
    
    private static bool _isPaused;

    public static void Update(double deltaTime)
    {
        DeltaTime = deltaTime;
        if (!IsPaused) TotalTime += deltaTime;
    }

    public static void SetPause()
    {
        
    }
}