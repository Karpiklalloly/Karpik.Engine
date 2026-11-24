namespace Karpik.Engine.Core;

/// <summary>Prepares ModSet-owned data after its scope is created, before creating a Simulation scope.</summary>
public interface IModSetPreparation
{
    ValueTask PrepareAsync(CancellationToken cancellationToken);
}
