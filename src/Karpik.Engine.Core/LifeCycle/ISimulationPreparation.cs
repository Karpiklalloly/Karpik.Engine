namespace Karpik.Engine.Core;

/// <summary>Prepares Simulation-owned services and runtime systems before pipeline construction.</summary>
public interface ISimulationPreparation
{
    ValueTask PrepareAsync(CancellationToken cancellationToken);
}
