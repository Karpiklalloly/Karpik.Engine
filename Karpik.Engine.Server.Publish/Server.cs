using System.Diagnostics;
using Karpik.Engine.Core;

namespace Karpik.Engine.Publish.Server;

public class Server
{
    public const int TICKS_PER_SECOND = 20;
    public const int SLEEP_TIME = 1000 / TICKS_PER_SECOND;
    private const double TICK_DT = 1.0 / TICKS_PER_SECOND;
    
    public void Start(Ref<bool> isRunning)
    {
        Bootstrap b = new();
        ModuleRegistry.RegisterAll(b);
        var mainThreadScheduler = b.Initialize(Environment.CurrentManagedThreadId, isRunning);
        
        var stopwatch = Stopwatch.StartNew();
        double nextTickTime = stopwatch.Elapsed.TotalSeconds;
        
        while (isRunning.Value)
        {
            double currentTime = stopwatch.Elapsed.TotalSeconds;
            int loops = 0;
            
            while (currentTime >= nextTickTime && loops < 5)
            {
                mainThreadScheduler.Execute();
                b.Loop(TICK_DT);
                nextTickTime += TICK_DT;
                loops++;
            }
            
            if (loops >= 5)
            {
                Console.WriteLine($"Server overloading! Skipping ticks. Lag: {currentTime - nextTickTime:F4}s");
                nextTickTime = currentTime + TICK_DT;
            }
            
            double timeToSleep = nextTickTime - stopwatch.Elapsed.TotalSeconds;
            if (timeToSleep > 0.001)
            {
                int sleepMs = (int)(timeToSleep * 1000);
                Thread.Sleep(sleepMs);
            }
            else
            {
                Thread.Yield(); 
            }
        }
        b.Shutdown();
    }
}