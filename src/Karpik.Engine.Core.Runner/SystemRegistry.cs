using System.Runtime.InteropServices;
using Autofac;

namespace Karpik.Engine.Core.Runner;

internal class SystemRegistry : ISystemRegistry
{
    private readonly List<SystemDescriptor> _descriptors = [];
    private readonly List<SystemInstanceDescriptor> _instanceDescriptors = [];
    private readonly List<Action<IBuilder>> _pipelineConfigurations = [];
    private readonly HashSet<Type> _registeredTypes = [];
    private readonly HashSet<string> _registeredIds = [];
    private readonly Dictionary<ISystem, string> _registeredInstances = new(ReferenceEqualityComparer.Instance);

    private bool _registrationsApplied;
    private bool _systemsResolved;
    private bool _typeRegistrationsSuppressed;

    public void ConfigurePipeline(Action<IBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_registrationsApplied)
        {
            throw new InvalidOperationException(
                "Pipeline configuration cannot be added after registrations were applied.");
        }

        _pipelineConfigurations.Add(configure);
    }

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

        _descriptors.Add(new SystemDescriptor(systemType, layer, order));
    }

    public void AddInstance(string systemId, ISystem system, string layer = "BASIC_LAYER", int order = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentNullException.ThrowIfNull(system);
        
        if (_systemsResolved)
        {
            throw new InvalidOperationException(
                "Systems cannot be added after system resolving were applied.");
        }
        
        if (_registeredIds.Contains(systemId))
        {
            throw new InvalidOperationException(
                $"System '{systemId}' was added more than once.");
        }

        if (_registeredInstances.ContainsKey(system))
            throw new InvalidOperationException("The same system instance cannot be registered more than once.");

        _registeredIds.Add(systemId);
        _registeredInstances.Add(system, systemId);
        
        _instanceDescriptors.Add(new SystemInstanceDescriptor(systemId, system, layer, order));
    }

    internal bool TryGetInstanceId(ISystem system, out string systemId)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (_registeredInstances.TryGetValue(system, out string? id))
        {
            systemId = id;
            return true;
        }
        systemId = string.Empty;
        return false;
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

        for (int i = 0; i < _pipelineConfigurations.Count; i++)
        {
            _pipelineConfigurations[i](builder);
        }

        for (int i = 0; i < descriptors.Length; i++)
        {
            ref readonly SystemDescriptor descriptor = ref descriptors[i];

            object system = services.Resolve(descriptor.SystemType);

            builder.Add(system, descriptor.Layer, descriptor.Order);
        }
        
        ReadOnlySpan<SystemInstanceDescriptor> instanceDescriptors = CollectionsMarshal.AsSpan(_instanceDescriptors);

        for (int i = 0; i < instanceDescriptors.Length; i++)
        {
            ref readonly SystemInstanceDescriptor descriptor = ref instanceDescriptors[i];

            builder.Add(descriptor.System, descriptor.Layer, descriptor.Order);
        }

        _systemsResolved = true;
    }
}
