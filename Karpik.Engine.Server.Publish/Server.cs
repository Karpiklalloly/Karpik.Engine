using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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

        ModuleLoader loader = new ModuleLoader();
        
        DiscoverAndRegisterModules(b, loader);
        
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
    
    private void DiscoverAndRegisterModules(Bootstrap bootstrap, ModuleLoader loader)
    {
#if DEBUG
        Console.WriteLine("[Launcher] DEBUG mode: Loading server modules from manifest...");
        loader.LoadServerModules();
        
        // Register all types from loaded assemblies
        var allTypes = loader.LoadedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .ToArray();
        
        bootstrap.RegisterTypes(allTypes);
        
        Console.WriteLine($"[Launcher] Registered {allTypes.Length} types from {loader.LoadedAssemblies.Length} assemblies");
#else
        Console.WriteLine("[Launcher] RELEASE mode: Registering server modules statically...");
        ModuleLoader.RegisterServerModules(bootstrap);
#endif
    }
}
