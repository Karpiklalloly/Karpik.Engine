using Autofac;
using Autofac.Builder;
using Karpik.Engine.Core;

namespace Karpik.Engine.Core.Runner;

/// <summary>
/// Collects generated <c>Register&lt;TService, TImplementation&gt;</c> calls and applies
/// them to Autofac builders per scope. Registrations are deduplicated by implementation
/// type: multiple export contracts of one implementation merge into a single Autofac
/// registration, so a shared singleton backs every contract (resolved decision #1).
/// </summary>
internal sealed class AutofacStaticServiceRegistry : IStaticServiceRegistry
{
    private readonly Dictionary<RegistrationKey, StaticRegistration> _registrations = new();

    public void Register<TService, TImplementation>(
        ModuleScope scope,
        ServiceLifetime lifetime,
        Func<IServiceResolver, TImplementation> factory)
        where TImplementation : class, TService
    {
        var key = new RegistrationKey(scope, typeof(TImplementation));
        if (!_registrations.TryGetValue(key, out StaticRegistration registration))
        {
            // Func<IServiceResolver, TImplementation> is covariant in TResult, so the
            // boxed delegate keeps application reflection-free and closure-free.
            Func<IServiceResolver, object> boxedFactory = factory;
            registration = new StaticRegistration(lifetime, boxedFactory);
            _registrations.Add(key, registration);
        }

        if (!registration.Contracts.Contains(typeof(TService)))
        {
            registration.Contracts.Add(typeof(TService));
        }
    }

    public void Apply(ContainerBuilder builder, ModuleScope scope)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (KeyValuePair<RegistrationKey, StaticRegistration> pair in _registrations)
        {
            if (pair.Key.Scope != scope)
            {
                continue;
            }

            StaticRegistration registration = pair.Value;
            IRegistrationBuilder<object, SimpleActivatorData, SingleRegistrationStyle> item =
                builder.Register((IComponentContext context) =>
                    registration.Factory(context.Resolve<IServiceResolver>()));
            item.AsSelf();
            item.As(registration.Contracts.ToArray());
            if (typeof(IStartable).IsAssignableFrom(pair.Key.ImplementationType))
            {
                // Mirrors AttributedServiceRegistrar: exposing the startable contract
                // makes Autofac activate the component when its owning scope builds.
                item.As<IStartable>();
            }
            switch (registration.Lifetime)
            {
                case ServiceLifetime.Singleton:
                    item.SingleInstance();
                    break;
                case ServiceLifetime.Transient:
                    item.InstancePerDependency();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(scope),
                        registration.Lifetime,
                        "Unsupported service lifetime.");
            }
        }
    }

    private readonly record struct RegistrationKey(ModuleScope Scope, Type ImplementationType);

    private sealed class StaticRegistration(ServiceLifetime lifetime, Func<IServiceResolver, object> factory)
    {
        public ServiceLifetime Lifetime { get; } = lifetime;

        public Func<IServiceResolver, object> Factory { get; } = factory;

        public List<Type> Contracts { get; } = [];
    }
}
