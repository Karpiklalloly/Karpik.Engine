using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Hot;

namespace Karpik.Engine.Client.Publish;

public class Client
{
    public void Start(Ref<bool> isRunning)
    {
        Bootstrap b = new();
        
#if DEBUG
        var clientAssemblies = ModuleLoader.SharedAssemblies.Concat(ModuleLoader.ClientOnlyAssemblies);
        HotReloadHandler.Initialize(clientAssemblies);
        
        b.ReloadModulesAction = () =>
        {
            ModuleLoader.LoadClientModules();
            return ModuleTypes();
        };
        
        b.GetAssembliesToScan = () => ModuleLoader.LoadedAssemblies;
#endif
        
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
        
#if DEBUG
        HotReloadHandler.Shutdown();
#endif
    }

    private void DiscoverAndRegisterModules(Bootstrap bootstrap)
    {
#if DEBUG
        ModuleLoader.LoadClientModules();
        var moduleTypes = ModuleTypes();

        foreach (var type in moduleTypes)
        {
            var moduleInstance = (IModule)Activator.CreateInstance(type)!;
            bootstrap.RegisterModule(moduleInstance);
        }
#else
        ModuleLoader.RegisterClientModules(bootstrap);
#endif
    }

    private Type[] ModuleTypes()
    {
        return ModuleLoader.LoadedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IModule).IsAssignableFrom(t) &&
                        t.GetCustomAttribute<ModuleAttribute>() != null).ToArray();
    }
}
