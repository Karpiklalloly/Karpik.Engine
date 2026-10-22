using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(ISimulationPreparation))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class LuaSimulationPreparation(
    IModsRegistry registry,
    IModsLifecycleManager lifecycle,
    Application application) : ISimulationPreparation
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
    }
}
