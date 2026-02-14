using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Core.ModuleManagement;

namespace Karpik.Engine.Core;

public class Runner : IRunner
{
    private readonly List<IModule> _modules = new();
    private EcsPipeline _pipeline = null!;
    private Time _time = new();
    private EcsServiceProvider _serviceProvider = null!;

    public void RegisterTypes(Type[] types)
    {
        foreach (var type in FilterTypesToModules(types))
        {
            var moduleInstance = (IModule)Activator.CreateInstance(type)!;
            RegisterModule(moduleInstance);
        }
    }

    void IRunner.Setup(ServiceProvider serviceProvider, bool hotReload, MainThreadScheduler scheduler, Action releaseOld, Type[]? newTypes = null, IRunner? oldRunner = null)
    {
        _serviceProvider = new EcsServiceProvider(serviceProvider);
        
        _pipeline?.Destroy();
        var newBuilder = EcsPipeline.New();
        newBuilder.AddModule(new JobSystemModule());
        newBuilder
            .Layers.Add(CustomLayers.BEGIN_PROGRAM_LAYER).Before(EcsConsts.PRE_BEGIN_LAYER).Back
            .Layers.Add(CustomLayers.END_PROGRAM_LAYER).After(EcsConsts.POST_END_LAYER);
        _serviceProvider.Register(_time);
        
        newBuilder.Inject(_serviceProvider);

        // EcsStaticCleaner.ResetAll();
        var oldModules = oldRunner?.GetModules() ?? [];
        if (hotReload)
        {
            Job.Wait();
            PreHotReload(oldModules);
        }
        
        Type[] types = [];
        if (hotReload && newTypes is not null)
        {
            types = FilterTypesToModules(newTypes);
        }
        Job.Initialize(new Jobs.JobSystem());

        scheduler.Schedule(() =>
        {
            var newModules = CreateModules(oldModules, types.ToDictionary(t => t.FullName ?? t.Name));
            if (hotReload)
            {
                _modules.Clear();
                newModules.ForEach(RegisterModule);
            }

            _modules.Sort((a, b) => GetPriority(a).CompareTo(GetPriority(b)));

            RegisterServices(_serviceProvider);
            // ConfigureAndAddModule(_serviceProvider, newBuilder);
            // var newPipeline = BuildPipeline(newBuilder, _serviceProvider);
            // AnotherModuleLoaded();

            if (hotReload)
            {
                HotReload(newModules, oldModules, _serviceProvider);
                Destroy(oldModules);
                oldRunner!.Destroy();
            }
            
            ConfigureAndAddModule(_serviceProvider, newBuilder);
            var newPipeline = BuildPipeline(newBuilder, _serviceProvider);
            AnotherModuleLoaded();

            _pipeline = newPipeline;
            InjectIntoSystems(newPipeline, _serviceProvider);
            _pipeline.Init();

            ConfigureComplete();

            if (hotReload)
            {
                scheduler.Schedule(releaseOld);
            }
        });
    }

    public void Run(double dt)
    {
        _time.Update(dt);
        _pipeline.Run();
    }

    public void Destroy()
    {
        _pipeline?.Destroy();
    }

    public List<IModule> GetModules()
    {
        return _modules;
    }

    public void RegisterModule(IModule module)
    {
        if (_modules.Any(m => m.GetType() == module.GetType()))
        {
            return;
        }

        _modules.Add(module);
    }

    private Type[] FilterTypesToModules(Type[] types)
    {
        var filter1 = types.Where(t => t.IsClass && !t.IsAbstract);
        var filter2 = filter1.Where(t => typeof(IModule).IsAssignableFrom(t) || typeof(IModule).IsAssignableTo(t));
        return filter2.Where(t => t.GetCustomAttribute<ModuleAttribute>() != null).ToArray();
    }
    
    private int GetPriority(IModule module)
    {
        var attr = module.GetType().GetCustomAttribute<ModuleAttribute>();
        return attr?.Priority ?? 0;
    }

    private void PreHotReload(List<IModule> oldModules)
    {
        foreach (var oldModule in oldModules)
        {
            if (oldModule is IModuleHotReload oldModuleHotReload)
            {
                oldModuleHotReload.OnPrepareHotReload();
            }
        }
    }

    private List<IModule> CreateModules(List<IModule> oldModules, Dictionary<string, Type> allNewModuleTypes)
    {
        var newModuleInstances = new List<IModule>();
        foreach (var oldModule in oldModules)
        {
            var oldModuleType = oldModule.GetType();
            if (allNewModuleTypes.TryGetValue(oldModuleType.FullName ?? oldModuleType.Name, out var newModuleType))
            {
                newModuleInstances.Add((IModule)Activator.CreateInstance(newModuleType)!);
            }
            else
            {
                Console.WriteLine($"[Bootstrap] Could not find new version of module {oldModuleType.FullName}. It will be skipped.");
            }
        }

        return newModuleInstances;
    }

    private void HotReload(List<IModule> newModules, List<IModule> oldModules, EcsServiceProvider serviceProvider)
    {
        List<(IModuleHotReload, IModule)> needToReload = [];
        for (int i = 0; i < newModules.Count; i++)
        {
            var newModule = newModules[i];
            var oldModule = oldModules[i];
            try
            {
                if (newModule is IModuleHotReload hotReloadableModule)
                {
                    if (!hotReloadableModule.OnHotReload(oldModule, serviceProvider))
                    {
                        needToReload.Add((hotReloadableModule, oldModule));
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Bootstrap] Failed to reload module {oldModule.GetType().FullName}: {e.Message}. It will be skipped.");
            }
        }
        
        foreach (var reload in needToReload)
        {
            reload.Item1.OnHotReload(reload.Item2, serviceProvider);
        }
    }

    private void Destroy(List<IModule> oldModules)
    {
        foreach (var oldModule in oldModules.OfType<IModuleDestroy>())
        {
            oldModule.Destroy();
        }
    }

    private void RegisterServices(EcsServiceProvider newServiceProvider)
    {
        foreach (var module in _modules)
        {
            module.OnRegisterServices(newServiceProvider);
        }
    }

    private void ConfigureAndAddModule(EcsServiceProvider newServiceProvider, EcsPipeline.Builder newBuilder)
    {
        foreach (var module in _modules.OfType<IModuleConfiguratable>())
        {
            module.OnConfigure(newServiceProvider, out var ecsModule);
            if (ecsModule is not null)
            {
                newBuilder.AddModule(ecsModule);
            }
        }
    }

    private void ConfigureComplete()
    {
        foreach (var module in _modules.OfType<IModuleConfiguratable>())
        {
            module.OnConfigureComplete(_serviceProvider);
        }
    }

    private void AnotherModuleLoaded()
    {
        var listeners = _modules.OfType<IModuleListener>().ToArray();
        foreach (var module in _modules)
        {
            foreach (var listener in listeners)
            {
                listener.OnAnotherModuleLoaded(_serviceProvider, module, module.GetType().Assembly);
            }
        }
    }

    private EcsPipeline BuildPipeline(EcsPipeline.Builder newBuilder, EcsServiceProvider newServiceProvider)
    {
        var newPipeline = newBuilder.Build(newServiceProvider);
        newServiceProvider.Register(newPipeline);
        newServiceProvider.InjectAll();
        return newPipeline;
    }

    private void InjectIntoSystems(EcsPipeline newPipeline, EcsServiceProvider newServiceProvider)
    {
        foreach (var system in newPipeline.AllSystems)
        {
            newServiceProvider.Inject(system);
        }

        foreach (var system in newPipeline.AllRunners)
        {
            newServiceProvider.Inject(system.Value);
        }
    }
}