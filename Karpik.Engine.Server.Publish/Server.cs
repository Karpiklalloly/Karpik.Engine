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
        
        var mainThreadScheduler = b.Initialize(Environment.CurrentManagedThreadId, isRunning, loader);
        
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
        
        var assembliesToScan = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => loader.SharedAssemblies.Contains(a.GetName().Name) || 
                        loader.ServerOnlyAssemblies.Contains(a.GetName().Name));

        var moduleTypes = assembliesToScan
            .SelectMany(assembly => assembly.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IModule).IsAssignableFrom(t) &&
                        t.GetCustomAttribute<ModuleAttribute>() != null);

        foreach (var type in moduleTypes)
        {
            try
            {
                var moduleInstance = (IModule)Activator.CreateInstance(type)!;
                bootstrap.RegisterModule(moduleInstance);
                Console.WriteLine($"[Launcher] Registered module: {type.Name}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Launcher] Failed to create instance of module {type.FullName}: {e.Message}");
            }
        }
#else
        Console.WriteLine("[Launcher] RELEASE mode: Registering server modules statically...");
        ModuleLoader.RegisterServerModules(bootstrap);
#endif
    }
}
