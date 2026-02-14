using System.Reflection;
using Karpik.Engine.Core.Hot;
using Karpik.Engine.Core.ModuleManagement;

namespace Karpik.Engine.Core;

public class Bootstrap
{
    public Func<Type[]> ReloadModulesAction { get; set; }
    public Func<IEnumerable<Assembly>> GetAssembliesToScan { get; set; }

    private ServiceProvider _serviceProvider = null!;

    private MainThreadScheduler _mainThreadScheduler = null!;
    private Ref<bool> _isRunning = null!;
    private Application _application = new();
    private IRunner _runner = null!;

    private ModuleLoader _loader;
    private IRunner? _oldRunner;

    public MainThreadScheduler Initialize(int mainTreadId, Ref<bool> isRunning, ModuleLoader loader)
    {
        _loader = loader;
        HotReloadHandler.OnUpdateApplication += OnCodeUpdate;

        _mainThreadScheduler = new MainThreadScheduler(mainTreadId);
        _isRunning = isRunning;
        
        _mainThreadScheduler.Schedule(() => Setup(false));
        return _mainThreadScheduler;
    }

    public void RegisterTypes(Type[] types)
    {
        if (_runner is null)
        {
            var filter1 = types.Where(t => t.IsClass && !t.IsAbstract);
            var filter2 = filter1.Where(x => x.FullName.Contains("Runner"));
            var type = filter2.FirstOrDefault(t =>
            {
                var a = typeof(IRunner).IsAssignableFrom(t);
                var b = typeof(IRunner).IsAssignableTo(t);
                return a || b;
            });
            if (type is null)
            {
                throw new Exception();
            }
            _runner = (IRunner)Activator.CreateInstance(type)!;
        }
        _runner.RegisterTypes(types);
    }

    private void OnCodeUpdate()
    {
        _mainThreadScheduler.Schedule(() => Setup(true));
    }

    private void Setup(bool hotReload)
    {
        _serviceProvider?.Destroy();
        Type[]? newTypes = null;
        if (hotReload)
        {
            _oldRunner = _runner;
            _runner = null!;
            newTypes = ReloadModulesAction.Invoke();
            
            RegisterTypes(newTypes);
        }
        _serviceProvider = new ServiceProvider();
        _serviceProvider.Register(_mainThreadScheduler);
        _serviceProvider.Register(_application);
        _runner.Setup(_serviceProvider, hotReload, _mainThreadScheduler, () =>
        {
            _oldRunner = null;
            HotReloadHandler.OnUpdateApplication -= OnCodeUpdate;
            _loader.CheckForPreviousContextUnload();
            HotReloadHandler.OnUpdateApplication += OnCodeUpdate;
        }, newTypes, _oldRunner);
    }

    public void Loop(double dt)
    {
        _runner.Run(dt);
        _isRunning.Value = _application.IsRunning;
    }

    public void Shutdown()
    {
        HotReloadHandler.OnUpdateApplication -= OnCodeUpdate;
        _runner.Destroy();
    }
}
