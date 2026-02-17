using System.Diagnostics;
using System.Reflection;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Hot;

namespace Karpik.Engine.Client.Publish;

public class Client
{
    public void Start(Ref<bool> isRunning)
    {
        Bootstrap b = new();
        
        ModuleLoader loader = new();
        var clientAssemblies = loader.SharedAssemblies.Concat(loader.ClientOnlyAssemblies);
        HotReloadHandler.Initialize(clientAssemblies);
        
        b.ReloadModulesAction = () =>
        {
            loader.LoadClientModules();
            return Types(loader);
        };
        
        b.GetAssembliesToScan = () => loader.LoadedAssemblies;
        
        DiscoverTypes(b, loader);
        
        var mainThreadScheduler = b.Initialize(Environment.CurrentManagedThreadId, isRunning, loader);
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
        HotReloadHandler.Shutdown();

    }

    private void DiscoverTypes(Bootstrap bootstrap, ModuleLoader loader)
    {
        loader.LoadClientModules();
        var types = Types(loader);
        bootstrap.RegisterTypes(types);
    }

    private Type[] Types(ModuleLoader loader)
    {
        return loader.LoadedAssemblies.SelectMany(assembly => assembly.GetTypes()).ToArray();
    }
}
