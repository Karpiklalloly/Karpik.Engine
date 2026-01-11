using System.Reflection;
using DCFApixels.DragonECS;

namespace Karpik.Engine.Core;

public class Bootstrap
{
    private ServiceProvider _serviceProvider = new();
    private readonly List<IModule> _modules = new(64);
    private Time _time = new();
    
    private MainThreadScheduler _mainThreadScheduler = null!;
    private Ref<bool> _isRunning = null!;
    private Application _application = new();
    private EcsPipeline _pipeline = null!;
    private EcsPipeline.Builder _builder = EcsPipeline.New();
    
    public void RegisterModule(IModule module)
    {
        _modules.Add(module);
    }

    public MainThreadScheduler Initialize(int mainTreadId, Ref<bool> isRunning)
    {
        _mainThreadScheduler = new MainThreadScheduler(mainTreadId);
        _isRunning = isRunning;
        Job.Initialize(new Jobs.JobSystem());
        _builder.AddModule(new JobSystemModule());
        _builder
            .Layers.Add(CustomLayers.BEGIN_PROGRAM_LAYER).Before(EcsConsts.PRE_BEGIN_LAYER).Back
            .Layers.Add(CustomLayers.END_PROGRAM_LAYER).After(EcsConsts.POST_END_LAYER);
        
        _serviceProvider.Register(_mainThreadScheduler);
        _serviceProvider.Register(_application);
        _serviceProvider.Register(_time);
        
        _modules.Sort((a, b) => GetPriority(a).CompareTo(GetPriority(b)));
        
        foreach (var module in _modules)
        {
            module.OnRegisterServices(_serviceProvider);
        }
        
        foreach (var module in _modules)
        {
            module.OnConfigure(_serviceProvider, out var ecsModule);
            if (ecsModule is not null)
            {
                _builder.AddModule(ecsModule);
            }
        }

        _builder.Inject(_serviceProvider);
        _pipeline = _builder.Build(_serviceProvider);
        _serviceProvider.Register(_pipeline);
        _serviceProvider.InjectAll();
        
        foreach (var system in _pipeline.AllSystems)
        {
            _serviceProvider.Inject(system);
        }
        
        foreach (var system in _pipeline.AllRunners)
        {
            _serviceProvider.Inject(system.Value);
        }
        
        _pipeline.Init();
        
        foreach (var sys in _modules)
        {
            sys.OnConfigureComplete(_serviceProvider);
            foreach (var module in _modules.OfType<IModuleListener>())
            {
                module.OnAnotherModuleLoaded(_serviceProvider, sys, sys.GetType().Assembly);
            }
        }

        return _mainThreadScheduler;
    }
    
    public void Loop(double dt)
    {
        _time.Update(dt);
        _pipeline.Run();
        _isRunning.Value = _application.IsRunning;
    }
    
    public void Shutdown()
    {
        _pipeline.Destroy();
    }

    private int GetPriority(IModule module)
    {
        var attr = module.GetType().GetCustomAttribute<ModuleAttribute>();
        return attr?.Priority ?? 0;
    }
}