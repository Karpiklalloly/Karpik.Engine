using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

internal sealed class LuaBindingsTestScope : IDisposable
{
    private readonly EcsDefaultWorld _world = new();
    private readonly ComponentCallbackScope _scope = new();
    private readonly ComponentSchemaRegistry _schemas;
    private readonly ComponentQueryPreparer _queries;
    private readonly LuaComponentRegistry _components;
    internal EcsDefaultWorld World => _world;

    internal LuaBindingsTestScope(LuaComponentRegistry components, ComponentSchemaRegistry? schemas = null)
    {
        _components = components; _schemas = schemas ?? new();
        _queries = new(_world, components.Bindings, _schemas, _scope);
    }

    internal LuaSimulationPreparation Create(IModsRegistry registry, IModsLifecycleManager lifecycle, Application application,
        ISystemRuntimeRegistry systems, Time time, ILogger<IScriptLoader>? logger = null) =>
        new(registry, lifecycle, application, systems, time, _components, _schemas, _queries, _scope,
            new ScriptSystemOptions(), logger ?? NullLogger<IScriptLoader>.Instance);

    public void Dispose() { _queries.Dispose(); _scope.Dispose(); _world.Destroy(); }

    internal float ReadTemperature(int entity)
    {
        var query = _queries.Prepare(new("inspect", [], [], [new("probe:Temperature")]), "test:inspect");
        _scope.Enter("test:inspect");
        try { return query.Components["probe:Temperature"].Get(entity).ReadF32(0); }
        finally { _scope.Exit(); }
    }
}

public sealed partial class LuaPreparationTests
{
    private static LuaComponentRegistry CreateLuaComponents() => new(new ClrComponentBindings(
        [new Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider()]));
}
