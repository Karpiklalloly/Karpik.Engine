using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Modding.Lua.Systems;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(ISimulationPreparation))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class LuaSimulationPreparation(
    IModsRegistry registry,
    IModsLifecycleManager lifecycle,
    Application application,
    ISystemRuntimeRegistry systems,
    Time time,
    LuaComponentRegistry components) : ISimulationPreparation
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
        components.Seal();
        foreach (ModContainer owner in lifecycle.Containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.Runtime is not LuaScriptRuntime runtime) continue;
            for (int index = 0; index < runtime.Systems.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LuaSystemDeclaration declaration = runtime.Systems[index];
                try
                {
                    var accesses = new List<EcsComponentAccessDescriptor>();
                    foreach (string name in declaration.Reads) accesses.Add(new(components.Resolve(name), EcsAccessMode.Read));
                    foreach (string name in declaration.Writes) accesses.Add(new(components.Resolve(name), EcsAccessMode.Write));
                    systems.AddInstance(new LuaUpdateSystem(owner, runtime, index, time),
                        new EcsUpdateSystemDescriptor(typeof(LuaUpdateSystem), true,
                            accesses.ToArray(), declaration.Orders, declaration.Id));
                }
                catch (InvalidDataException error)
                {
                    throw new InvalidDataException($"System '{declaration.Id}': {error.Message}", error);
                }
            }
        }
    }
}
