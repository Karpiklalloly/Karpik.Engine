using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Core;

/// <summary>A Simulation-owned backend that can prepare a replacement without changing active gameplay state.</summary>
public interface ISimulationCodeReloadParticipant
{
    ReadOnlyMemory<ISystemUpdate> ActiveSystems { get; }
    ValueTask<ISimulationCodeReloadCandidate> PrepareReloadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Owns an inactive code revision. The host validates its complete execution graph before
/// PrepareActivation, then calls Activate without running user code. Dispose rejects an
/// uncommitted candidate; after Activate its lifetime belongs to the participant.
/// </summary>
public interface ISimulationCodeReloadCandidate : IDisposable
{
    ReadOnlyMemory<ISystemUpdate> Systems { get; }
    ReadOnlyMemory<EcsUpdateSystemDescriptor> Descriptors { get; }
    void PrepareActivation(CancellationToken cancellationToken);
    ReadOnlyMemory<byte> SharedSchemaProof => default;
    int? MaximumModReplicaRowBytes => null;
    void Activate();
    void RetirePrevious();
}
