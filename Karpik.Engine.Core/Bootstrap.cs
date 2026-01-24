using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Core.Hot;
using Karpik.Engine.Core.ModuleManagement;

namespace Karpik.Engine.Core;

public class Bootstrap
{
    public Action? ReloadModulesAction { get; set; }
    public Func<IEnumerable<Assembly>>? GetAssembliesToScan { get; set; }

    private ServiceProvider _serviceProvider = new();
    private readonly List<IModule> _modules = new();
    private Time _time = new();

    private MainThreadScheduler _mainThreadScheduler = null!;
    private Ref<bool> _isRunning = null!;
    private Application _application = new();
    private EcsPipeline _pipeline = null!;

    public void RegisterModule(IModule module)
    {
        if (_modules.Any(m => m.GetType() == module.GetType()))
        {
            return;
        }

        _modules.Add(module);
    }

    public MainThreadScheduler Initialize(int mainTreadId, Ref<bool> isRunning)
    {
        HotReloadHandler.OnUpdateApplication += OnCodeUpdate;

        _mainThreadScheduler = new MainThreadScheduler(mainTreadId);
        _isRunning = isRunning;
        Job.Initialize(new Jobs.JobSystem());

        RebuildAndSwapPipelineInternal();

        return _mainThreadScheduler;
    }

    private void OnCodeUpdate()
    {
        _mainThreadScheduler.Schedule(HotReloadModulesAndRebuildPipeline);
    }

    private void HotReloadModulesAndRebuildPipeline()
    {
        Console.WriteLine("[Bootstrap] Hot reload detected. Updating modules...");
        var oldModules = new List<IModule>(_modules);

#if DEBUG
        Job.Wait();
        
        foreach (var oldModule in oldModules)
        {
            if (oldModule is IModuleHotReload oldModuleHotReload)
            {
                oldModuleHotReload.OnPrepareHotReload();
            }
        }
        
        Job.JobSystem.Shutdown();
        Job.Initialize(new Jobs.JobSystem());
        Console.WriteLine("[Bootstrap-DEBUG] All jobs are complete.");

        var oldAssemblies = _modules.Select(m => m.GetType().Assembly).Distinct().ToList();
        
        Console.WriteLine("[Bootstrap-DEBUG] Performing full assembly reload...");
        ReloadModulesAction?.Invoke();
#endif
        
        var newModuleInstances = new List<IModule>();
        
        var newAssemblies = GetAssembliesToScan?.Invoke() ?? AppDomain.CurrentDomain.GetAssemblies();
        var typeMapper = new TypeMapper(oldAssemblies, newAssemblies);

        var allNewModuleTypes = newAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IModule).IsAssignableFrom(t) &&
                        t.GetCustomAttribute<ModuleAttribute>() != null)
            .ToDictionary(t => t.FullName ?? t.Name);

        List<(IModuleHotReload, IModule)> needToReload = [];

        foreach (var oldModule in oldModules)
        {
            var oldModuleType = oldModule.GetType();
            if (allNewModuleTypes.TryGetValue(oldModuleType.FullName ?? oldModuleType.Name, out var newModuleType))
            {
                try
                {
                    var newModuleInstance = (IModule)Activator.CreateInstance(newModuleType)!;

                    if (newModuleInstance is IModuleHotReload hotReloadableModule)
                    {
                        Console.WriteLine($"[Bootstrap] Transferring state for module {newModuleType.Name} via IModuleHotReload...");
                        if (!hotReloadableModule.OnHotReload(oldModule, typeMapper))
                        {
                            needToReload.Add((hotReloadableModule, oldModule));
                        }
                    }

                    newModuleInstances.Add(newModuleInstance);
                }
                catch (Exception e)
                {
                    Console.WriteLine(
                        $"[Bootstrap] Failed to reload module {oldModuleType.FullName}: {e.Message}. It will be skipped.");
                }
            }
            else
            {
                Console.WriteLine(
                    $"[Bootstrap] Could not find new version of module {oldModuleType.FullName}. It will be skipped.");
            }
        }
        
        foreach (var reload in needToReload)
        {
            reload.Item1.OnHotReload(reload.Item2, typeMapper);
        }

        foreach (var oldModule in oldModules.Where(x => x is IModuleDestroy).Cast<IModuleDestroy>())
        {
            oldModule.Destroy();
        }
        
        _pipeline?.Destroy();
        _pipeline = null!;
        Console.WriteLine("[Bootstrap-DEBUG] Old pipeline destroyed.");

        _modules.Clear();
        _modules.AddRange(newModuleInstances);

        RebuildAndSwapPipelineInternal();

#if DEBUG
        typeMapper.ClearCache();
        _mainThreadScheduler.Schedule(ModuleLoader.CheckForPreviousContextUnload);
        
#endif

        Console.WriteLine("[Bootstrap] Module update complete.");
    }

    private void RebuildAndSwapPipelineInternal()
    {
        Console.WriteLine("[Bootstrap] Rebuilding pipeline and service provider...");

        _pipeline?.Destroy();

        var newServiceProvider = new ServiceProvider();
        var newBuilder = EcsPipeline.New();

        newServiceProvider.Register(_mainThreadScheduler);
        newServiceProvider.Register(_application);
        newServiceProvider.Register(_time);

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

        _serviceProvider.Destroy();
        _serviceProvider = newServiceProvider;
        _pipeline = newPipeline;

        _pipeline.Init();

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
