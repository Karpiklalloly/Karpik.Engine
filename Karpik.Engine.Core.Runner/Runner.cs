using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Core.ModuleManagement;

namespace Karpik.Engine.Core;

/// <summary>
/// Engine runner that manages modules and the ECS pipeline.
/// </summary>
public class EngineRunner : IRunner
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

    void IRunner.Setup(ServiceProvider serviceProvider, bool hotReload, MainThreadScheduler scheduler, Type[]? newTypes = null, Dictionary<string, byte[]> hotReloadData = null)
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
        
        Type[] types = [];
        if (hotReload && newTypes is not null)
        {
            types = FilterTypesToModules(newTypes);
        }

        scheduler.Schedule(() =>
        {
            var newModules = CreateModules(types);
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
                HotReload(newModules, _serviceProvider, hotReloadData);
            }
            else if (hotReloadData != null && hotReloadData.Count > 0)
            {
                // Process isolation: apply initial state from previous worker after fresh start
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
        _pipeline?.Destroy();
        _pipeline = null;
        _modules.Clear();
    }

    public Dictionary<string, byte[]> GetHotReloadData()
    {
        return PreHotReload(GetModules());
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

    private Dictionary<string, byte[]> PreHotReload(List<IModule> oldModules)
    {
        Dictionary<string, byte[]> hotReloadInfo = [];
        foreach (var oldModule in oldModules)
        {
            if (oldModule is IModuleHotReload oldModuleHotReload)
            {
                string name = oldModule.GetType().FullName ?? oldModule.GetType().Name;
                hotReloadInfo[name] = oldModuleHotReload.OnPrepareHotReload();
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

    private void HotReload(List<IModule> newModules, EcsServiceProvider serviceProvider, Dictionary<string, byte[]> hotReloadInfo)
    {
        List<(IModuleHotReload, byte[])> needToReload = [];
        for (int i = 0; i < newModules.Count; i++)
        {
            var newModule = newModules[i];
            try
            {
                if (newModule is IModuleHotReload hotReloadableModule)
                {
                    string name = newModule.GetType().FullName ?? newModule.GetType().Name;
                    if (!hotReloadableModule.OnHotReload(hotReloadInfo[name], serviceProvider))
                    {
                        needToReload.Add((hotReloadableModule, hotReloadInfo[name]));
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Bootstrap] Failed to reload module {newModule.GetType().FullName}: {e.Message}. It will be skipped.");
            }
        }
        
        foreach (var reload in needToReload)
        {
            reload.Item1.OnHotReload(reload.Item2, serviceProvider);
        }
    }
    
    /// <summary>
    /// Applies initial state from previous worker process (process isolation).
    /// Unlike HotReload, this doesn't replace modules - it just restores state to existing ones.
    /// </summary>
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
                        // OnHotReload returns false if it needs to be called again
                        // (ECSInstaller uses this pattern: first call sets _reloaded=true, second call restores state)
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
        
        // Second pass for modules that need two calls
        foreach (var reload in needToReload)
        {
            try
            {
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