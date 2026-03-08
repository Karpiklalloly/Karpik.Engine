using System.Reflection;
using DCFApixels.DragonECS;

namespace Karpik.Engine.Core;

public class EngineRunner : IEngineRunner
{
    private readonly List<IModule> _modules = new();
    private EcsPipeline _pipeline = null!;
    private Time _time = new();
    private EcsServiceProvider _serviceProvider = null!;
    private Application _application;

    public void RegisterTypes(Type[] types)
    {
        foreach (var type in FilterTypesToModules(types))
        {
            var moduleInstance = (IModule)Activator.CreateInstance(type)!;
            RegisterModule(moduleInstance);
        }
    }

    public void Setup(Application application, MainThreadScheduler scheduler, Dictionary<string, byte[]>? hotReloadData = null)
    {
        _application = application;
        _serviceProvider = new EcsServiceProvider(new ServiceProvider());
        _serviceProvider.Register(scheduler);
        _serviceProvider.Register(_application);
        
        var newBuilder = EcsPipeline.New();
        newBuilder.AddModule(new JobSystemModule());
        newBuilder
            .Layers.Add(CustomLayers.BEGIN_PROGRAM_LAYER).Before(EcsConsts.PRE_BEGIN_LAYER).Back
            .Layers.Add(CustomLayers.END_PROGRAM_LAYER).After(EcsConsts.POST_END_LAYER);
        _serviceProvider.Register(_time);
        
        newBuilder.Inject(_serviceProvider);

        scheduler.Schedule(() =>
        {
            _modules.Sort((a, b) => GetPriority(a).CompareTo(GetPriority(b)));

            RegisterServices(_serviceProvider);

            if (hotReloadData is { Count: > 0 })
            {
                Console.WriteLine("[Runner] Applying initial state from previous worker process");
                ApplyInitialState(_modules, _serviceProvider, hotReloadData);
            }
            
            ConfigureAndAddModule(_serviceProvider, newBuilder);
            var newPipeline = BuildPipeline(newBuilder, _serviceProvider);
            AnotherModuleLoaded();

            _pipeline = newPipeline;
            InjectIntoSystems(newPipeline, _serviceProvider);
            _pipeline.Init();

            ConfigureComplete();
        });
    }

    public void Run(double dt)
    {
        _time.Update(dt);
        _pipeline.Run();
    }

    public void Destroy()
    {
        Destroy(GetModules());
        _pipeline.Destroy();
        _pipeline = null;
        _modules.Clear();
    }

    public Dictionary<string, byte[]> GetHotReloadData()
    {
        return PreHotReload(GetModules(), _serviceProvider);
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

        Console.WriteLine($"Register module {module.Name}");
        _modules.Add(module);
    }

    private Type[] FilterTypesToModules(Type[] types)
    {
        var classTypes = types.Where(t => t.IsClass && !t.IsAbstract);
        var moduleTypes = classTypes.Where(t => typeof(IModule).IsAssignableFrom(t) || typeof(IModule).IsAssignableTo(t));
        var withAttr = moduleTypes.Where(t => t.GetCustomAttribute<ModuleAttribute>() != null);
        return withAttr.ToArray();
    }
    
    private int GetPriority(IModule module)
    {
        var attr = module.GetType().GetCustomAttribute<ModuleAttribute>();
        return attr?.Priority ?? 0;
    }

    private Dictionary<string, byte[]> PreHotReload(List<IModule> oldModules, EcsServiceProvider newServiceProvider)
    {
        Dictionary<string, byte[]> hotReloadInfo = [];
        foreach (var oldModule in oldModules)
        {
            if (oldModule is IModuleHotReload oldModuleHotReload)
            {
                string name = oldModule.GetType().FullName ?? oldModule.GetType().Name;
                hotReloadInfo[name] = oldModuleHotReload.OnPrepareHotReload(newServiceProvider);
            }
        }

        return hotReloadInfo;
    }

    private List<IModule> CreateModules(Type[] allNewModuleTypes)
    {
        var newModuleInstances = new List<IModule>();
        foreach (var type in allNewModuleTypes)
        {
            newModuleInstances.Add((IModule)Activator.CreateInstance(type)!);
        }

        return newModuleInstances;
    }

    private void ApplyInitialState(List<IModule> modules, EcsServiceProvider serviceProvider, Dictionary<string, byte[]> stateData)
    {
        List<(IModuleHotReload, byte[])> needToReload = [];
        
        foreach (var module in modules)
        {
            try
            {
                if (module is IModuleHotReload hotReloadableModule)
                {
                    string name = module.GetType().FullName ?? module.GetType().Name;
                    if (stateData.TryGetValue(name, out var data))
                    {
                        Console.WriteLine($"On Hot Reload module {hotReloadableModule.Name}");
                        if (!hotReloadableModule.OnHotReload(data, serviceProvider))
                        {
                            needToReload.Add((hotReloadableModule, data));
                        }
                        Console.WriteLine($"[Runner] Applied initial state to module: {name}");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Runner] Failed to apply initial state to module {module.GetType().FullName}: {e.Message}");
            }
        }
        
        foreach (var reload in needToReload)
        {
            try
            {
                Console.WriteLine($"On Hot Reload module {reload.Item1.Name}");
                reload.Item1.OnHotReload(reload.Item2, serviceProvider);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Runner] Failed to apply initial state on second pass: {e.Message}");
            }
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
            Console.WriteLine($"On Register Services for module {module.Name}");
            module.OnRegisterServices(newServiceProvider);
        }
    }

    private void ConfigureAndAddModule(EcsServiceProvider newServiceProvider, EcsPipeline.Builder newBuilder)
    {
        foreach (var module in _modules.OfType<IModuleConfiguratable>())
        {
            Console.WriteLine($"On Configure module {module.Name}");
            module.OnConfigure(newServiceProvider, newServiceProvider);
            var ecsModule = newServiceProvider.Get<IEcsModule>();
            if (ecsModule is not null)
            {
                Console.WriteLine($"Got module {ecsModule.GetType().Name}");
                newBuilder.AddModule(ecsModule);
            }
            newServiceProvider.Forget<IEcsModule>();
        }
    }

    private void ConfigureComplete()
    {
        foreach (var module in _modules.OfType<IModuleConfiguratable>())
        {
            Console.WriteLine($"On Configure Complete {module.Name}");
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
                Console.WriteLine($"On Another Module Loaded module {module.Name}. Listener {listener.Name}");
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