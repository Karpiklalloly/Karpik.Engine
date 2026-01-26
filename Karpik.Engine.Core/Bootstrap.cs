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
    public Func<Type[]> ReloadModulesAction { get; set; }
    public Func<IEnumerable<Assembly>> GetAssembliesToScan { get; set; }

    private ServiceProvider _serviceProvider = null!;
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
        
        _mainThreadScheduler.Schedule(() => Setup(false));
        return _mainThreadScheduler;
    }

    private void OnCodeUpdate()
    {
        _mainThreadScheduler.Schedule(() => Setup(true));
    }

    private void Setup(bool hotReload)
    {
        _pipeline?.Destroy();
        var newBuilder = EcsPipeline.New();
        newBuilder.AddModule(new JobSystemModule());
        newBuilder
            .Layers.Add(CustomLayers.BEGIN_PROGRAM_LAYER).Before(EcsConsts.PRE_BEGIN_LAYER).Back
            .Layers.Add(CustomLayers.END_PROGRAM_LAYER).After(EcsConsts.POST_END_LAYER);
        
        _serviceProvider?.Destroy();
        _serviceProvider = CreateServiceProvider(newBuilder);
        
        List<IModule> oldModules = [];
        if (hotReload)
        {
            oldModules = new List<IModule>(_modules);
        }

#if DEBUG
        if (hotReload)
        {
            Job.Wait();
            PreHotReload(oldModules);
        }
        
        var oldAssemblies = _modules.Select(m => m.GetType().Assembly).Distinct().ToList();
        Type[] types = [];
        if (hotReload)
        {
            types = ReloadModulesAction.Invoke();
        }
#endif
        Job.Initialize(new Jobs.JobSystem());

        _mainThreadScheduler.Schedule(() =>
        {
            var typeMapper = new TypeMapper(oldAssemblies, GetAssembliesToScan.Invoke());

            var newModules = CreateModules(oldModules, types.ToDictionary(t => t.FullName ?? t.Name));
            if (hotReload)
            {
                _modules.Clear();
                newModules.ForEach(RegisterModule);
            }

            _modules.Sort((a, b) => GetPriority(a).CompareTo(GetPriority(b)));

            RegisterServices(_serviceProvider);
            ConfigureAndAddModule(_serviceProvider, newBuilder);
            var newPipeline = BuildPipeline(newBuilder, _serviceProvider);
            AnotherModuleLoaded();

            if (hotReload)
            {
                HotReload(newModules, oldModules, typeMapper, _serviceProvider);
                Destroy(oldModules);
            }

            _pipeline = newPipeline;
            InjectIntoSystems(newPipeline, _serviceProvider);
            _pipeline.Init();

            ConfigureComplete();

            typeMapper.ClearCache();
#if DEBUG
            if (hotReload)
            {
                _mainThreadScheduler.Schedule(() =>
                {
                    HotReloadHandler.OnUpdateApplication -= OnCodeUpdate;
                    ModuleLoader.CheckForPreviousContextUnload();
                    HotReloadHandler.OnUpdateApplication += OnCodeUpdate;
                });
            }
#endif
        });
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

    private ServiceProvider CreateServiceProvider(EcsPipeline.Builder newBuilder)
    {
        var newServiceProvider = new ServiceProvider();
        newServiceProvider.Register(_mainThreadScheduler);
        newServiceProvider.Register(_application);
        newServiceProvider.Register(_time);
        
        newBuilder.Inject(newServiceProvider);
        return newServiceProvider;
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

    private void HotReload(List<IModule> newModules, List<IModule> oldModules, TypeMapper typeMapper, ServiceProvider serviceProvider)
    {
        List<(IModuleHotReload, IModule)> needToReload = [];
        for (int i = 0; i < newModules.Count; i++)
        {
            var newModule = newModules[i];
            var oldModule = oldModules[i];
            var oldModuleType = oldModule.GetType();
            try
            {
                if (newModule is IModuleHotReload hotReloadableModule)
                {
                    if (!hotReloadableModule.OnHotReload(oldModule, typeMapper, serviceProvider))
                    {
                        needToReload.Add((hotReloadableModule, oldModule));
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Bootstrap] Failed to reload module {oldModuleType.FullName}: {e.Message}. It will be skipped.");
            }
        }
        
        foreach (var reload in needToReload)
        {
            reload.Item1.OnHotReload(reload.Item2, typeMapper, serviceProvider);
        }
    }

    private void Destroy(List<IModule> oldModules)
    {
        foreach (var oldModule in oldModules.OfType<IModuleDestroy>())
        {
            oldModule.Destroy();
        }
    }

    private void RegisterServices(ServiceProvider newServiceProvider)
    {
        foreach (var module in _modules)
        {
            module.OnRegisterServices(newServiceProvider);
        }
    }

    private void ConfigureAndAddModule(ServiceProvider newServiceProvider, EcsPipeline.Builder newBuilder)
    {
        foreach (var module in _modules)
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
        foreach (var module in _modules)
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

    private EcsPipeline BuildPipeline(EcsPipeline.Builder newBuilder, ServiceProvider newServiceProvider)
    {
        var newPipeline = newBuilder.Build(newServiceProvider);
        newServiceProvider.Register(newPipeline);
        newServiceProvider.InjectAll();
        return newPipeline;
    }

    private void InjectIntoSystems(EcsPipeline newPipeline, ServiceProvider newServiceProvider)
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
