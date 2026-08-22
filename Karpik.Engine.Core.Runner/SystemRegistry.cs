using System.Runtime.InteropServices;
using Autofac;

namespace Karpik.Engine.Core.Runner;

internal class SystemRegistry : ISystemRegistry
{
    private readonly List<SystemDescriptor> _descriptors = [];
    private readonly HashSet<Type> _registeredTypes = [];

    private bool _registrationsApplied;
    private bool _systemsResolved;
    private bool _typeRegistrationsSuppressed;

    public void Add<TSystem>(string layer = "BASIC_LAYER", int order = 0) where TSystem : class, ISystem
    {
        if (_registrationsApplied)
        {
            throw new InvalidOperationException(
                "Systems cannot be added after Autofac registrations were applied.");
        }

        Type systemType = typeof(TSystem);

        if (!_registeredTypes.Add(systemType))
        {
            throw new InvalidOperationException(
                $"System '{systemType.FullName}' was added more than once.");
        }

        _descriptors.Add(new SystemDescriptor(
            systemType,
            layer,
            order));
    }

    /// <summary>
    /// Static mode: systems are activated through generated typed factories instead of
    /// Autofac reflection-based type registration. Descriptors are kept for pipeline
    /// ordering; the Dynamic overload behavior remains unchanged when this is not set.
    /// </summary>
    public void SuppressTypeRegistrations()
    {
        if (_registrationsApplied)
        {
            throw new InvalidOperationException(
                "System registrations were already applied.");
        }

        _typeRegistrationsSuppressed = true;
    }

    public void RegisterTypes(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (_registrationsApplied)
        {
            throw new InvalidOperationException(
                "System registrations were already applied.");
        }

        if (!_typeRegistrationsSuppressed)
        {
            for (int i = 0; i < _descriptors.Count; i++)
            {
                builder.RegisterType(_descriptors[i].SystemType)
                    .AsSelf()
                    .InstancePerLifetimeScope();
            }
        }

        _registrationsApplied = true;
    }
    
    public void ResolveAndAdd(
        IBuilder builder,
        IServiceResolver services)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(services);

        if (!_registrationsApplied)
        {
            throw new InvalidOperationException(
                "System types must be registered before resolving systems.");
        }

        if (_systemsResolved)
        {
            throw new InvalidOperationException(
                "Systems were already added to the pipeline.");
        }

        ReadOnlySpan<SystemDescriptor> descriptors = CollectionsMarshal.AsSpan(_descriptors);

        for (int i = 0; i < descriptors.Length; i++)
        {
            ref readonly SystemDescriptor descriptor = ref descriptors[i];

            object system = services.Resolve(descriptor.SystemType);

            builder.Add(
                system,
                descriptor.Layer,
                descriptor.Order);
        }

        _systemsResolved = true;
    }
}