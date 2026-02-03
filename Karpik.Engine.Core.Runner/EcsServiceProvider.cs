using DCFApixels.DragonECS;

namespace Karpik.Engine.Core;

public class EcsServiceProvider : IServiceRegister, IServiceContainer, IInjectionBlock
{
    private readonly ServiceProvider _serviceProvider;

    public EcsServiceProvider(ServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IServiceRegister Register<T>(T service) where T : class
    {
        return _serviceProvider.Register(service);
    }

    public IServiceRegister Register(Type serviceType, object service)
    {
        return _serviceProvider.Register(serviceType, service);
    }

    public object? GetService(Type serviceType)
    {
        return _serviceProvider.GetService(serviceType);
    }

    public T Get<T>() where T : class
    {
        return _serviceProvider.Get<T>();
    }

    public void Forget<T>() where T : class
    {
        _serviceProvider.Forget<T>();
    }

    public void InjectTo(Injector inj)
    {
        foreach (var services in _serviceProvider.Services.Values.Where(x => x.First() is not IInjectionBlock and not ServiceProvider))
        {
            inj.Inject(services.First());
        }
    }
    
    public Span<T> GetAll<T>() where T : class
    {
        return _serviceProvider.GetAll<T>();
    }
    
    public void InjectAll()
    {
        _serviceProvider.InjectAll();
    }
}