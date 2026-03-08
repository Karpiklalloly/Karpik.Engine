using System.Collections.Concurrent;

namespace Karpik.Engine.Core;

public class ServiceProvider : IServiceRegister, IServiceContainer
{
    public ConcurrentDictionary<Type, List<object>> Services => _services;

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

    public T? Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
            return (T)service.FirstOrDefault();

        Console.WriteLine($"Not found service {typeof(T)}");
        return null;
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

    public void InjectAll()
    {
        foreach (var value in _services.Values.SelectMany(x => x))
        {
            this.Inject(value);
        }
    }

    public void Destroy()
    {
        _services.Clear();
    }
}