using System.Composition;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Modding.Bindings;

namespace Karpik.Engine.Shared.Modding.Lua;

/// <summary>Explicit names for CLR component metadata exposed to Lua system declarations.</summary>
[Export(typeof(LuaComponentRegistry))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class LuaComponentRegistry(ClrComponentBindings bindings)
{
    public ClrComponentBindings Bindings => bindings;

    public void Register<T>(string name) where T : unmanaged, IEcsComponent
    {
        bindings.Register<T>(name);
    }

    internal void Seal() => bindings.Seal();

    internal Type Resolve(string name) => bindings.ResolveType(name);
}
