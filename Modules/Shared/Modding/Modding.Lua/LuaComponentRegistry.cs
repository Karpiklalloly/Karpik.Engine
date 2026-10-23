using System.Composition;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding.Lua;

/// <summary>Explicit names for CLR component metadata exposed to Lua system declarations.</summary>
[Export(typeof(LuaComponentRegistry))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class LuaComponentRegistry
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);
    private bool _sealed;

    public void Register<T>(string name) where T : unmanaged, IEcsComponent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_sealed) throw new InvalidOperationException("Lua component registrations are closed after preparation.");
        if (!_types.TryAdd(name, typeof(T)))
            throw new InvalidOperationException($"Lua component '{name}' is already registered.");
    }

    internal void Seal() => _sealed = true;

    internal Type Resolve(string name) => _types.TryGetValue(name, out Type? type)
        ? type : throw new InvalidDataException($"Unknown Lua component '{name}'.");
}
