using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Core;

/// <summary>
/// Receives the generated ECS registry provider instances during static
/// composition so the Static startup path never enumerates assemblies
/// reflectively (<c>Assembly.GetTypes</c>/<c>Activator.CreateInstance</c>).
/// The Dynamic composition path keeps its discovery machinery and never
/// implements or consumes this contract.
/// </summary>
public interface IStaticEcsRegistryProviders
{
    void AddUpdate(IEcsUpdateRegistryProvider provider);

    void AddRenderPrepare(IEcsRenderPrepareRegistryProvider provider);
}
