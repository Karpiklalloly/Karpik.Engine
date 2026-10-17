using Autofac;

namespace Karpik.Engine.Core;

public class AutofacServiceResolver(ILifetimeScope scope) : IServiceResolver
{
    public T Resolve<T>() where T : notnull
    {
        return scope.Resolve<T>();
    }

    public object Resolve(Type serviceType)
    {
        return scope.Resolve(serviceType);
    }

    public IEnumerable<T> ResolveAll<T>()
    {
        return scope.Resolve<IEnumerable<T>>();
    }

    public object? GetService(Type serviceType)
    {
        return scope.ResolveOptional(serviceType);
    }
}