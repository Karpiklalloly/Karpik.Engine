namespace Karpik.Engine.Shared.Network.Core;

/// <summary>One fixed cohort of ordered markers. Only the application knows when its queued work and effects have finished.</summary>
public sealed class RevisionDrainBarrier
{
    private readonly IPeer[] _peers;
    private readonly ulong _all;
    private ulong _seen;

    public RevisionDrainBarrier(IPeer[] peers, long operationId, long revision, byte kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        if (peers.Length is 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(peers));
        if (kind is not (RevisionGameplayControl.InputCutoff or RevisionGameplayControl.EffectCutoff or RevisionGameplayControl.BaselineApplied))
            throw new ArgumentOutOfRangeException(nameof(kind));
        _peers = (IPeer[])peers.Clone();
        for (int i = 0; i < _peers.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(_peers[i]);
            for (int j = 0; j < i; j++)
                if (ReferenceEquals(_peers[i], _peers[j])) throw new ArgumentException("Duplicate cohort peer.", nameof(peers));
        }
        OperationId = operationId;
        Revision = revision;
        Kind = kind;
        _all = ulong.MaxValue >> (64 - _peers.Length);
    }

    internal ReadOnlySpan<IPeer> Peers => _peers;
    public long OperationId { get; }
    public long Revision { get; }
    public byte Kind { get; }
    public bool AllMarkersObserved => _seen == _all;
    public bool ApplicationDrained { get; private set; }

    public bool TryObserve(IPeer peer, byte kind, long operationId, long revision, int payloadBytes)
    {
        if (kind != Kind || operationId != OperationId || revision != Revision || payloadBytes != 0)
            return false;
        for (int i = 0; i < _peers.Length; i++)
        {
            if (!ReferenceEquals(peer, _peers[i])) continue;
            ulong bit = 1UL << i;
            if ((_seen & bit) != 0) return false;
            _seen |= bit;
            return true;
        }
        return false;
    }

    /// <summary>Call only after mandatory callbacks, structural playback and resulting reliable sends have completed.</summary>
    public bool TryConfirmApplicationDrained()
    {
        if (!AllMarkersObserved || ApplicationDrained) return false;
        ApplicationDrained = true;
        return true;
    }
}

public static class RevisionGameplayControl
{
    public const byte InputCutoff = 16;
    public const byte EffectCutoff = 17;
    public const byte BaselineChunk = 18;
    public const byte BaselineApplied = 19;
    public const byte Resume = 20;

    /// <summary>Stop client input and place the marker behind all accepted required sends.</summary>
    public static bool TrySendInputCutoff(IRevisionTransitionNetwork network, IPeer server, long operationId, long revision) =>
        operationId > 0 && revision > 0 && network.GameplayRevision == revision && network.GameplaySendingEnabled &&
        network.GameplayReceivingEnabled && network.TrySetGameplayGates(false, true) &&
        network.TrySendRevisionControl(server, InputCutoff, operationId, revision, []);

    /// <summary>Cut off effects only after accepted input/effects for the entire cohort has drained.</summary>
    public static bool TrySendEffectCutoff(IRevisionTransitionNetwork network, RevisionDrainBarrier input)
    {
        if (input.Kind != InputCutoff || !input.ApplicationDrained || network.GameplayRevision != input.Revision ||
            !network.GameplaySendingEnabled || !network.GameplayReceivingEnabled || !network.TrySetGameplayGates(false, true))
            return false;
        return SendMarkers(network, input, EffectCutoff);
    }

    /// <summary>After every client acknowledges application, open receive, enqueue Resume, then open send.</summary>
    /// <remarks>A partial send is a post-commit failure, not permission to roll back or resume a subset.</remarks>
    public static bool TryResumeServer(IRevisionTransitionNetwork network, RevisionDrainBarrier applied)
    {
        if (applied.Kind != BaselineApplied || !applied.AllMarkersObserved || network.GameplayRevision != applied.Revision ||
            network.GameplaySendingEnabled || network.GameplayReceivingEnabled || !network.TrySetGameplayGates(false, true))
            return false;
        if (SendMarkers(network, applied, Resume))
            return network.TrySetGameplayGates(true, true);
        network.TrySetGameplayGates(false, false);
        return false;
    }

    private static bool SendMarkers(IRevisionTransitionNetwork network, RevisionDrainBarrier cohort, byte kind)
    {
        foreach (var peer in cohort.Peers)
            if (!network.TrySendRevisionControl(peer, kind, cohort.OperationId, cohort.Revision, []))
                return false;
        return true;
    }
}
