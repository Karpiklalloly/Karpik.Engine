namespace Karpik.Engine.Core;

public interface IServiceResolver : IServiceProvider
{
    public T Resolve<T>() where T : notnull;

    public object Resolve(Type serviceType);

    public IEnumerable<T> ResolveAll<T>();
}