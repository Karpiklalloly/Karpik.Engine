using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Publish;

public class Client
{
    public void Start(Ref<bool> isRunning)
    {
        Bootstrap b = new();
        
        DiscoverAndRegisterModules(b);
        
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

    private void DiscoverAndRegisterModules(Bootstrap bootstrap)
    {
#if DEBUG
        Console.WriteLine("[Launcher] DEBUG mode: Loading client modules from manifest...");
        ModuleLoader.LoadClientModules();

        // ИСПРАВЛЕНИЕ 2: Искать сборки в новом списке ModuleLoader.LoadedAssemblies
        var assembliesToScan = ModuleLoader.LoadedAssemblies;
        
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
        Console.WriteLine("[Launcher] RELEASE mode: Registering client modules statically...");
        ModuleLoader.RegisterClientModules(bootstrap);
#endif
    }
}
