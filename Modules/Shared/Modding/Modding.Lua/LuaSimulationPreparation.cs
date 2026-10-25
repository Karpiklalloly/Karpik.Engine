using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Modding.Lua.Systems;
using Karpik.Engine.Shared.Modding.Bindings;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(ISimulationPreparation))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class LuaSimulationPreparation(
    IModsRegistry registry,
    IModsLifecycleManager lifecycle,
    Application application,
    ISystemRuntimeRegistry systems,
    Time time,
    LuaComponentRegistry components,
    ComponentSchemaRegistry schemaRegistry,
    ComponentQueryPreparer queryPreparer,
    ComponentCallbackScope callbackScope,
    ScriptSystemOptions options,
    ILogger<IScriptLoader> logger) : ISimulationPreparation
{
    public async ValueTask PrepareAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecutionSide side = application.ApplicationSide switch
        {
            Side.Client => ExecutionSide.Client,
            Side.Server => ExecutionSide.Server,
            _ => throw new InvalidOperationException("Unsupported application side.")
        };
        await lifecycle.Load(registry.GetMods().ToArray(), side, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var schemas = new List<ComponentSchema>();
        foreach (ModContainer owner in lifecycle.Containers)
            if (owner.Runtime is LuaScriptRuntime runtime) schemas.AddRange(runtime.Components);
        foreach (var schema in schemas)
            if (components.Bindings.TryResolveType(schema.Id, out _)) throw new InvalidDataException($"Dynamic schema '{schema.Id}' conflicts with a CLR component registration.");
        schemaRegistry.ValidateAndFreeze(schemas.ToArray());
        queryPreparer.Initialize();
        components.Seal();
        foreach (ModContainer owner in lifecycle.Containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.Runtime is not LuaScriptRuntime runtime) continue;
            runtime.PrepareEcs(queryPreparer, callbackScope, options, logger);
            for (int index = 0; index < runtime.Systems.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LuaSystemDeclaration declaration = runtime.Systems[index];
                try
                {
                    systems.AddInstance(new LuaUpdateSystem(owner, runtime, index, time),
                        new EcsUpdateSystemDescriptor(typeof(LuaUpdateSystem), true,
                            runtime.GetContext(index).Accesses, declaration.Orders, declaration.Id));
                }
                catch (InvalidDataException error)
                {
                    throw new InvalidDataException($"System '{declaration.Id}': {error.Message}", error);
                }
            }
        }
    }
}
