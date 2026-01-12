using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.DragonECS;

namespace Karpik.Engine.Core;

public class Bootstrap
{
    private ServiceProvider _serviceProvider = new();
    private readonly List<IModule> _modules = new();
    private Time _time = new();
    
    private MainThreadScheduler _mainThreadScheduler = null!;
    private Ref<bool> _isRunning = null!;
    private Application _application = new();
    private EcsPipeline _pipeline = null!;
    
    public void RegisterModule(IModule module)
    {
        _modules.Add(module);
    }

    public MainThreadScheduler Initialize(int mainTreadId, Ref<bool> isRunning)
    {
        HotReloadHandler.OnUpdateApplication += OnCodeUpdate;
        
        _mainThreadScheduler = new MainThreadScheduler(mainTreadId);
        _isRunning = isRunning;
        Job.Initialize(new Jobs.JobSystem());
        
        // Initial build
        RebuildAndSwapPipelineInternal();

        return _mainThreadScheduler;
    }

    private void OnCodeUpdate(Type[]? obj)
    {
        _mainThreadScheduler.Schedule(RequestPipelineRebuild);
    }

    private void RequestPipelineRebuild()
    {
        RebuildAndSwapPipelineInternal();
    }

    private void RebuildAndSwapPipelineInternal()
    {
        Job.Wait();
        Console.WriteLine("[Bootstrap] Rebuilding pipeline and service provider...");

        // 1. Save old instances for later disposal
        var oldPipeline = _pipeline;
        oldPipeline?.Destroy(); // DANGER: This destroys the graphics context. Disabled for now.
        
        // 2. Create new core components
        var newServiceProvider = new ServiceProvider();
        var newBuilder = EcsPipeline.New();
        
        // 3. Re-register all essential services
        newServiceProvider.Register(_mainThreadScheduler);
        newServiceProvider.Register(_application);
        newServiceProvider.Register(_time);
        
        // 4. Re-process all modules
        _modules.Sort((a, b) => GetPriority(a).CompareTo(GetPriority(b)));
        
        foreach (var module in _modules)
        {
            module.OnRegisterServices(newServiceProvider);
        }
        
        newBuilder.AddModule(new JobSystemModule());
        newBuilder.Layers.Add(CustomLayers.BEGIN_PROGRAM_LAYER).Before(EcsConsts.PRE_BEGIN_LAYER).Back
                  .Layers.Add(CustomLayers.END_PROGRAM_LAYER).After(EcsConsts.POST_END_LAYER);
        
        foreach (var module in _modules)
        {
            module.OnConfigure(newServiceProvider, out var ecsModule);
            if (ecsModule is not null)
            {
                newBuilder.AddModule(ecsModule);
            }
        }

        // 5. Build and inject the new pipeline
        newBuilder.Inject(newServiceProvider);
        var newPipeline = newBuilder.Build(newServiceProvider);
        newServiceProvider.Register(newPipeline);
        newServiceProvider.InjectAll();
        
        foreach (var system in newPipeline.AllSystems)
        {
            newServiceProvider.Inject(system);
        }
        
        foreach (var system in newPipeline.AllRunners)
        {
            newServiceProvider.Inject(system.Value);
        }
        
        // 6. Atomically swap to the new instances
        _serviceProvider = newServiceProvider;
        _pipeline = newPipeline;
        
        // 7. Initialize the new pipeline
        _pipeline.Init();
        
        // 8. Finalize configuration, notify listeners, and dispose of the old pipeline
        foreach (var module in _modules)
        {
            module.OnConfigureComplete(_serviceProvider);
            foreach (var listener in _modules.OfType<IModuleListener>())
            {
                listener.OnAnotherModuleLoaded(_serviceProvider, module, module.GetType().Assembly);
            }
        }

        Console.WriteLine("[Bootstrap] Pipeline rebuild complete.");
    }
    
    public void Loop(double dt)
    {
        _time.Update(dt);
        _pipeline.Run();
        _isRunning.Value = _application.IsRunning;
    }
    
    public void Shutdown()
    {
        HotReloadHandler.OnUpdateApplication -= OnCodeUpdate;
        _pipeline?.Destroy();
    }

    private int GetPriority(IModule module)
    {
        var attr = module.GetType().GetCustomAttribute<ModuleAttribute>();
        return attr?.Priority ?? 0;
    }
}
