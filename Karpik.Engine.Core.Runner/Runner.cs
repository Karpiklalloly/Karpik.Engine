using System.Reflection;
using DCFApixels.DragonECS;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.DragonECS;

namespace Karpik.Engine.Core;

public class EngineRunner : IEngineRunner
{
    private const string EcsHotReloadInstallerFullName = "Karpik.Engine.Shared.ECS.ECSInstaller";
    private readonly List<IInstaller> _modules = new();
    private EcsPipeline _pipeline = null!;
    private Time _time = new();
    private EcsServiceProvider _serviceProvider = null!;
    private Application _application;
    
    // Runners
    private EcsBeginRunner _beginRunner = null!;
    private EcsFixedRunner _fixedRunner = null!;
    private EcsUpdateRunner _updateRunner = null!;
    private EcsLateRunner _lateRunner = null!;
    private EcsRenderRunner _renderRunner = null!;
    private FixedRunTicker _fixedRunTicker = null!;

    public void RegisterTypes(Type[] types)
    {
        foreach (var type in FilterTypesToModules(types))
        {
            var moduleInstance = (IInstaller)Activator.CreateInstance(type)!;
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
        var newModuleBuilder = new Builder(newBuilder);
        newBuilder.AddModule(new JobSystemModule());
        newBuilder
            .Layers.Add(CustomLayers.BEGIN_PROGRAM_LAYER).Before(EcsConsts.PRE_BEGIN_LAYER).Back
            .Layers.Add(CustomLayers.END_PROGRAM_LAYER).After(EcsConsts.POST_END_LAYER);
        _serviceProvider.Register(_time);
        
        newBuilder.Inject<IServiceContainer>(_serviceProvider);
        newBuilder.Inject<IServiceRegister>(_serviceProvider);
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
            
            ConfigureAndAddModule(_serviceProvider, newModuleBuilder);
            var newPipeline = BuildPipeline(newBuilder, _serviceProvider);
            AnotherModuleLoaded();

            _pipeline = newPipeline;
            InjectIntoSystems(newPipeline, _serviceProvider);
            _pipeline.Init();
            _beginRunner = _pipeline.GetRunner<EcsBeginRunner>();
            _fixedRunner = _pipeline.GetRunner<EcsFixedRunner>();
            _updateRunner = _pipeline.GetRunner<EcsUpdateRunner>();
            _lateRunner = _pipeline.GetRunner<EcsLateRunner>();
            _renderRunner = _pipeline.GetRunner<EcsRenderRunner>();
            _fixedRunTicker = new FixedRunTicker(_fixedRunner, _application);

            ConfigureComplete();
        });
    }

    public void Run(double dt)
    {
        _time.Update(dt);
        _beginRunner.BeginRun();
        _pipeline.Run();
        _fixedRunTicker.FixedRun();
        _updateRunner.Update();
        _lateRunner.LateRun();
        _renderRunner.Render();
        
    }

    public void Destroy()
    {
        Destroy(GetModules());
        _pipeline.Destroy();
        _pipeline = null;
        _modules.Clear();
        _fixedRunTicker.Destroy();
    }

    public Dictionary<string, byte[]> GetHotReloadData()
    {
        return PreHotReload(GetModules(), _serviceProvider);
    }

    public List<IInstaller> GetModules()
    {
        return _modules;
    }

    public void RegisterModule(IInstaller installer)
    {
        if (_modules.Any(m => m.GetType() == installer.GetType()))
        {
            return;
        }

        Console.WriteLine($"Register module {installer.Name}");
        _modules.Add(installer);
    }

    private Type[] FilterTypesToModules(Type[] types)
    {
        var classTypes = types.Where(t => t.IsClass && !t.IsAbstract);
        var moduleTypes = classTypes.Where(t => typeof(IInstaller).IsAssignableFrom(t) || typeof(IInstaller).IsAssignableTo(t));
        var withAttr = moduleTypes.Where(t => t.GetCustomAttribute<ModuleAttribute>() != null);
        return withAttr.ToArray();
    }
    
    private int GetPriority(IInstaller installer)
    {
        var attr = installer.GetType().GetCustomAttribute<ModuleAttribute>();
        return attr?.Priority ?? 0;
    }

    private Dictionary<string, byte[]> PreHotReload(List<IInstaller> oldModules, EcsServiceProvider newServiceProvider)
    {
        Dictionary<string, byte[]> hotReloadInfo = [];
        foreach (var oldModule in oldModules)
        {
            var name = oldModule.GetType().FullName ?? oldModule.GetType().Name;
            if (name != EcsHotReloadInstallerFullName)
            {
                continue;
            }

            if (oldModule is IInstallerHotReload oldModuleHotReload)
            {
                hotReloadInfo[name] = oldModuleHotReload.OnPrepareHotReload(newServiceProvider);
            }
        }

        return hotReloadInfo;
    }

    private List<IInstaller> CreateModules(Type[] allNewModuleTypes)
    {
        var newModuleInstances = new List<IInstaller>();
        foreach (var type in allNewModuleTypes)
        {
            newModuleInstances.Add((IInstaller)Activator.CreateInstance(type)!);
        }

        return newModuleInstances;
    }

    private void ApplyInitialState(List<IInstaller> modules, EcsServiceProvider serviceProvider, Dictionary<string, byte[]> stateData)
    {
        List<(IInstallerHotReload, byte[])> needToReload = [];
        
        foreach (var module in modules)
        {
            try
            {
                if (module is IInstallerHotReload hotReloadableModule)
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

    private void Destroy(List<IInstaller> oldModules)
    {
        foreach (var oldModule in oldModules.OfType<IInstallerDestroy>())
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

    private void ConfigureAndAddModule(EcsServiceProvider newServiceProvider, IBuilder newBuilder)
    {
        foreach (var installer in _modules.OfType<IInstallerConfiguratable>())
        {
            Console.WriteLine($"On Configure module {installer.Name}");
            installer.OnConfigure(newServiceProvider, newServiceProvider, out IModule? module);
            if (module is not null)
            {
                Console.WriteLine($"Got module {module.GetType().Name}");
                module.Import(newBuilder);
            }
        }
    }

    private void ConfigureComplete()
    {
        foreach (var module in _modules.OfType<IInstallerConfiguratable>())
        {
            Console.WriteLine($"On Configure Complete {module.Name}");
            module.OnConfigureComplete(_serviceProvider);
        }
    }

    private void AnotherModuleLoaded()
    {
        var listeners = _modules.OfType<IInstallerListener>().ToArray();
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
        newBuilder.AddRunner<EcsBeginRunner>();
        newBuilder.AddRunner<EcsFixedRunner>();
        newBuilder.AddRunner<EcsUpdateRunner>();
        newBuilder.AddRunner<EcsLateRunner>();
        newBuilder.AddRunner<EcsRenderRunner>();
        var newPipeline = newBuilder.Build(newServiceProvider);
        newServiceProvider.Register(newPipeline.Injector);
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
