namespace Karpik.Engine.Core;

public interface IStaticServiceRegistry
{
    void Register<TService, TImplementation>(
        ModuleScope scope,
        ServiceLifetime lifetime,
        Func<IServiceResolver, TImplementation> factory)
        where TImplementation : class, TService;
}
