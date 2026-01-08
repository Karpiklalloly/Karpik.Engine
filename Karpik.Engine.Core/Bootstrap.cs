using System.Reflection;
using DCFApixels.DragonECS;

namespace Karpik.Engine.Core;

public class Bootstrap
{
    private ServiceProvider _serviceProvider = new();
    
    private MainThreadScheduler _mainThreadScheduler = null!;
    private Ref<bool> _isRunning = null!;
    private Application _application = new();
    private EcsPipeline _pipeline = null!;
    private EcsPipeline.Builder _builder = EcsPipeline.New();

    public MainThreadScheduler Initialize(int mainTreadId, Ref<bool> isRunning)
    {
        _mainThreadScheduler = new MainThreadScheduler(mainTreadId);
        _isRunning = isRunning;
        Job.Initialize(new Jobs.JobSystem());
        _builder.AddModule(new JobSystemModule());
        
        var modules = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => (IModule)Activator.CreateInstance(t)!)
            .ToArray();
        
        _serviceProvider.Register(_mainThreadScheduler);
        _serviceProvider.Register(_application);

        _builder
            .Layers.Add(CustomLayers.BEGIN_PROGRAM_LAYER).Before(EcsConsts.PRE_BEGIN_LAYER).Back
            .Layers.Add(CustomLayers.END_PROGRAM_LAYER).After(EcsConsts.POST_END_LAYER);

        foreach (var sys in modules)
        {
            sys.OnRegisterServices(_serviceProvider);
            sys.OnConfigure(_serviceProvider, out var module);
            if (module is not null)
            {
                _builder.AddModule(module);
            }
        }

        _pipeline = _builder.BuildAndInit(_serviceProvider);
        _serviceProvider.Register(_pipeline);
        _serviceProvider.InjectAll();
        
        foreach (var system in _pipeline.AllSystems)
        {
            _serviceProvider.Inject(system);
        }
        
        foreach (var sys in modules)
        {
            sys.OnConfigureComplete(_serviceProvider);
            foreach (var module in modules.OfType<IModuleListener>())
            {
                module.OnAnotherModuleLoaded(_serviceProvider, sys, sys.GetType().Assembly);
            }
        }

        return _mainThreadScheduler;
    }
    
    public void Loop(double dt)
    {
        Time.Update(dt);
        _pipeline.Run();
        _isRunning.Value = _application.IsRunning;
    }
    
    public void Shutdown()
    {
        _pipeline.Destroy();
    }
}