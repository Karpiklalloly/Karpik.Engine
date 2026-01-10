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

        var directory = Directory.GetCurrentDirectory();
        var dllPaths = Directory.EnumerateFiles(directory, "*.dll")
            .Where(f => !string.Equals(f, Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase));

        var dlls = dllPaths
            .Select(path =>
            {
                // если сборка уже загружена — используем её
                var already = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a =>
                    {
                        try { return string.Equals(a.Location, path, StringComparison.OrdinalIgnoreCase); }
                        catch { return false; } // некоторые динамические сборки могут бросать
                    });

                return already ?? Assembly.LoadFrom(path);
            })
            .ToArray();

        List<IModule> modulesList = [];
        foreach (var dll in dlls)
        {
            modulesList.AddRange(Load(dll));
        }
        Sort(modulesList);
        var modules = modulesList.ToArray();
        modulesList.Clear();
        
        _serviceProvider.Register(_mainThreadScheduler);
        _serviceProvider.Register(_application);

        _builder
            .Layers.Add(CustomLayers.BEGIN_PROGRAM_LAYER).Before(EcsConsts.PRE_BEGIN_LAYER).Back
            .Layers.Add(CustomLayers.END_PROGRAM_LAYER).After(EcsConsts.POST_END_LAYER);

        foreach (var sys in modules)
        {
            sys.OnRegisterServices(_serviceProvider);
        }
        
        foreach (var sys in modules)
        {
            sys.OnConfigure(_serviceProvider, out var module);
            if (module is not null)
            {
                _builder.AddModule(module);
            }
        }

        _builder.Inject(_serviceProvider);
        _pipeline = _builder.BuildAndInit(_serviceProvider);
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

    private IEnumerable<IModule> Load(Assembly assembly)
    {
        var types = assembly.GetTypes();
        var where = types.Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
        var modules = where
            .Select(t => (IModule)Activator.CreateInstance(t)!);
        return modules;
    }
    
    private void Sort(List<IModule> modules)
    {
        modules.Sort((a, b) =>
            a.GetType().GetCustomAttributes(typeof(ModuleAttribute), true).OfType<ModuleAttribute>().First().Priority.CompareTo(
            b.GetType().GetCustomAttributes(typeof(ModuleAttribute), true).OfType<ModuleAttribute>().First().Priority));
    }
}