namespace Karpik.Engine.Core;

/// <summary>A network composition cannot run independent local code reload.</summary>
public sealed class NetworkedSimulation;

#if DEBUG
/// <summary>Debug preparation backend; admission must precede execution of any captured artifact.</summary>
public interface ISimulationSchemaReloadParticipant : ISimulationCodeReloadParticipant
{
    bool SupportsSchemaReload { get; }
    ValueTask<IPreparedSchemaReload> PrepareSchemaReloadAsync(Func<string, bool> admitLocalArtifact, CancellationToken token);
}

public interface IPreparedSchemaReload : ISimulationCodeReloadCandidate
{
    // Exact digest of prepared Shared inputs and the resulting mod schema. Invalid until activation is prepared.
    new ReadOnlyMemory<byte> SharedSchemaProof { get; }
}

/// <summary>Trusted host-only lifecycle handle, not a gameplay service. Wire authority belongs to the caller.</summary>
public interface ISimulationSchemaReloadHost
{
    Side Side { get; }
    bool IsRunning { get; }
    SimulationCodeReloadStatus TryReserve(long id, long basis, long target, out ISimulationSchemaReload? reload);
    void FailStop();
}

public interface ISimulationSchemaReload
{
    Task<SimulationCodeReloadResult> Ready { get; }
    Task<SimulationCodeReloadResult> Committed { get; }
    Task<SimulationCodeReloadResult> Completion { get; }
    ReadOnlyMemory<byte> SharedSchemaProof { get; }
    int? MaximumModReplicaRowBytes { get; }
    bool TryPrepare(Func<string, bool> admitLocalArtifact);
    bool TryCommit();
    bool TryAbort();
    bool TryResume();
    /// <summary>Release capture/refresh reservation after the network has settled.</summary>
    void Release();
}
#endif
