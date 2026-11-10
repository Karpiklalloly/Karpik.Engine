using System.Composition;
using Karpik.Engine.Core;

using DCFApixels.DragonECS;

namespace Karpik.Engine.Shared.Modding.Bindings;

[Export(typeof(ClrComponentBindings))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class ClrComponentBindings
{
    private readonly Dictionary<Type, ClrComponentBindingDescriptor> _bindings = new();
    private readonly Dictionary<string, Type> _names = new(StringComparer.Ordinal);
    private bool _sealed;

    public ClrComponentBindings(IEnumerable<IClrComponentBindingProvider> providers)
    {
        foreach (IClrComponentBindingProvider provider in providers)
        {
            foreach (ClrComponentBindingDescriptor binding in provider.GetBindings().Span)
                _bindings.TryAdd(binding.ComponentType, binding);
        }
    }

    public void Register<T>(string id) where T : unmanaged, IEcsComponent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_sealed) throw new InvalidOperationException("CLR component registrations are closed after preparation.");
        if (!_names.TryAdd(id, typeof(T))) throw new InvalidOperationException($"Component '{id}' is already registered.");
    }

    public void Seal() => _sealed = true;
    public Type ResolveType(string id) => _names.TryGetValue(id, out Type? type)
        ? type : throw new InvalidDataException($"Unknown component '{id}'.");

    public bool TryResolveType(string id, out Type? type) => _names.TryGetValue(id, out type);

    public ClrComponentBindingDescriptor ResolveBinding(string id)
    {
        Type type = ResolveType(id);
        return _bindings.TryGetValue(type, out ClrComponentBindingDescriptor? binding)
            ? binding : throw new InvalidDataException($"Component '{id}' has no generated field binding. Compile its registration with the bindings generator.");
    }
}
