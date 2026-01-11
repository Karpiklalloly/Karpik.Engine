using System.Diagnostics;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Publish;

public class Client
{
    public void Start(Ref<bool> isRunning)
    {
        Bootstrap b = new();
        ModuleRegistry.RegisterAll(b);
        var mainThreadScheduler = b.Initialize(Environment.CurrentManagedThreadId, isRunning);
        var stopwatch = Stopwatch.StartNew();
        double lastTime = 0;
        while (isRunning.Value)
        {
            double currentTime = stopwatch.Elapsed.TotalSeconds;
            double deltaTime = currentTime - lastTime;
            if (deltaTime > 0.1) deltaTime = 0.1;
            
            mainThreadScheduler.Execute();
            b.Loop(deltaTime);
        }
        b.Shutdown();
    }
}