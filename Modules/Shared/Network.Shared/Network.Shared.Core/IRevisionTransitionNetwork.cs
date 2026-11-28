namespace Karpik.Engine.Shared.Network.Core;

/// <summary>Optional transport handoff; capability is not authorization or application drain.</summary>
/// <remarks>Use on the simulation/maintenance owner. Handoff only after outstanding send jobs and the prior pump finish.</remarks>
public interface IRevisionTransitionNetwork : INetworkManager
{
    // Fits in one LiteNetLib packet at the starting MTU, including both headers.
    const int MaxControlPayloadBytes = 1000;

    /// <summary>The payload is borrowed for the synchronous callback only. Consumers validate the kind and operation state.</summary>
    delegate void RevisionControlHandler(IPeer peer, byte kind, long operationId, long revision, ReadOnlySpan<byte> payload);

    event RevisionControlHandler? RevisionControlReceived;
    long GameplayRevision { get; }
    bool GameplaySendingEnabled { get; }
    bool GameplayReceivingEnabled { get; }

    /// <summary>True only with both gates open and at least one admitted peer; every connected peer must support the revision wire protocol.</summary>
    bool CanBeginRevisionTransition { get; }

    void ConfigureRevisionTransitions(long initialRevision);

    /// <summary>Snapshot all supported, admitted peers and hold startup admission until release.</summary>
    bool TryReserveRevisionCohort(Span<IPeer> peers, out int count) { count = 0; return false; }
    void ReleaseRevisionCohort() { }

    /// <summary>Close send first to drain. With receive closed, PollEvents routes control but no gameplay or gameplay lifecycle callbacks.</summary>
    /// <remarks>A later frame in the same PollEvents may dispatch as soon as receive is reopened. Reopen only when application callbacks are safe.</remarks>
    bool TrySetGameplayGates(bool sendOpen, bool receiveOpen);

    /// <summary>Advance monotonically from the expected revision with both gates closed. The caller owns the commit decision.</summary>
    bool TryAdvanceGameplayRevision(long expectedRevision, long nextRevision);
    bool TrySendRevisionControl(IPeer peer, byte kind, long operationId, long revision, ReadOnlySpan<byte> payload);
}
