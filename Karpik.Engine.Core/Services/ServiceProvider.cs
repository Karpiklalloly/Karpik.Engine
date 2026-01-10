using System.Collections.Concurrent;
using DCFApixels.DragonECS;

namespace Karpik.Engine.Core;

public class ServiceProvider : IServiceRegister, IServiceContainer, IInjectionBlock
{
    private readonly ConcurrentDictionary<Type, List<object>> _services = new();

    public ServiceProvider()
    {
        Register<IServiceRegister>(this);
        Register<IServiceContainer>(this);
    }
    
    public IServiceRegister Register<T>(T service) where T : class
    {
        Register(typeof(T), service);
        if (service.GetType() != typeof(T))
        {
            Register(service.GetType(), service);
        }
        return this;
    }

    public IServiceRegister Register(Type serviceType, object service)
    {
        if (!_services.ContainsKey(serviceType))
        {
            _services[serviceType] = [];
        }
        
        _services[serviceType].Add(service);
        return this;
    }

    public T Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
            return (T)service.First();
        
        throw new Exception($"Service {typeof(T).Name} not found!");
    }

    public void Forget<T>() where T : class
    {
        _services.TryRemove(typeof(T), out _);
    }

    public Span<T> GetAll<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
            return service.Cast<T>().ToArray().AsSpan();
        
        throw new Exception($"Service {typeof(T).Name} not found!");
    }

    public object? GetService(Type serviceType)
    {
        return _services.GetValueOrDefault(serviceType)?.First();
    }

    public void InjectTo(Injector inj)
    {
        foreach (var services in _services.Values.Where(x => x.First() is not IInjectionBlock))
        {
            inj.Inject(services.First());
        }
    }

    public void InjectAll()
    {
        foreach (var value in _services.Values.SelectMany(x => x))
        {
            this.Inject(value);
        }
    }
}