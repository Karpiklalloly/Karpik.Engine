using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding.Lua.Systems;

public sealed class InitSystem(
    IModsRegistry registry,
    IModsLifecycleManager lifecycle,
    Application application)
    : ISystemAsyncInit
{
    public async ValueTask InitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        ExecutionSide side = application.ApplicationSide switch
        {
            Side.Client => ExecutionSide.Client,
            Side.Server => ExecutionSide.Server,
            _ => throw new InvalidOperationException("Unsupported application side.")
        };

        await registry.Scan(side);

        cancellationToken.ThrowIfCancellationRequested();

        // TODO: ID order for dependency-free prototype; replace with dependency order.
        ModDefinition[] definitions = registry.GetMods()
            .OrderBy(mod => mod.MetaData.Id, StringComparer.Ordinal)
            .ToArray();

        await lifecycle.Load(definitions, side);

        cancellationToken.ThrowIfCancellationRequested();
    }
}

public sealed class BeginSystem(IModsLifecycleManager lifecycle) : ISystemBegin
{
    public void Begin()
    {
        IReadOnlyList<ModContainer> containers = lifecycle.Containers;

        for (int i = 0; i < containers.Count; i++)
        {
            containers[i].Start();
        }
    }
}