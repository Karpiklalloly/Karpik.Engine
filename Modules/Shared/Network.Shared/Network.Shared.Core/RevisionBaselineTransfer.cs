using System.Buffers.Binary;

namespace Karpik.Engine.Shared.Network.Core;

/// <summary>One receive-side baseline assembled on the reliable control stream. Owned by the current transition.</summary>
public sealed class RevisionBaselineTransfer(RevisionReplicaBaseline baseline, IPeer server, long operationId, long revision)
{
    private const int ChunkHeaderBytes = 2 * sizeof(int);
    private const int DataBytesPerChunk = IRevisionTransitionNetwork.MaxControlPayloadBytes - ChunkHeaderBytes;
    private byte[]? _body;
    private int _received;
    private bool _failed;
    private bool _acknowledged;

    public bool Complete => !_failed && _body is not null && _received == _body.Length;

    public static bool TrySend(IRevisionTransitionNetwork network, IPeer peer, long operationId, long revision, RevisionReplicaBaseline baseline)
    {
        var body = baseline.Encoded.Span;
        if (operationId <= 0 || revision <= 0 || network.GameplayRevision != revision ||
            network.GameplaySendingEnabled || network.GameplayReceivingEnabled || body.Length < RevisionReplicaBaseline.HeaderBytes ||
            BinaryPrimitives.ReadInt64LittleEndian(body) != revision)
            return false;

        Span<byte> frame = stackalloc byte[IRevisionTransitionNetwork.MaxControlPayloadBytes];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        for (int offset = 0; offset < body.Length;)
        {
            int length = Math.Min(DataBytesPerChunk, body.Length - offset);
            BinaryPrimitives.WriteInt32LittleEndian(frame[sizeof(int)..], offset);
            body.Slice(offset, length).CopyTo(frame[ChunkHeaderBytes..]);
            if (!network.TrySendRevisionControl(peer, RevisionGameplayControl.BaselineChunk, operationId, revision, frame[..(ChunkHeaderBytes + length)]))
                return false;
            offset += length;
        }
        return true;
    }

    public bool TryReceiveChunk(IPeer peer, byte kind, long operation, long frameRevision, ReadOnlySpan<byte> payload)
    {
        if (kind != RevisionGameplayControl.BaselineChunk || !Matches(peer, operation, frameRevision) || _failed || _acknowledged)
            return false;
        if (payload.Length is <= ChunkHeaderBytes or > IRevisionTransitionNetwork.MaxControlPayloadBytes || Complete)
            return Fail();

        int total = BinaryPrimitives.ReadInt32LittleEndian(payload);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(payload[sizeof(int)..]);
        var data = payload[ChunkHeaderBytes..];
        if (total is < RevisionReplicaBaseline.HeaderBytes or > RevisionReplicaBaseline.MaxBytes ||
            offset != _received || (_body is not null && total != _body.Length) ||
            data.Length != Math.Min(DataBytesPerChunk, total - offset))
            return Fail();
        _body ??= new byte[total];
        data.CopyTo(_body.AsSpan(offset));
        _received += data.Length;
        return true;
    }

    /// <summary>Called at the quiescent application boundary, not as a receipt for the last bytes.</summary>
    public bool TryApplyAndAcknowledge(IRevisionTransitionNetwork network, Action clearOldGameplayCaches)
    {
        if (!Complete || _acknowledged || network.GameplayRevision != revision || network.GameplayReceivingEnabled ||
            network.GameplaySendingEnabled)
            return false;
        if (!baseline.TryApply(_body, revision)) return Fail();
        clearOldGameplayCaches();
        if (!network.TrySendRevisionControl(server, RevisionGameplayControl.BaselineApplied, operationId, revision, []))
            return Fail();
        _acknowledged = true;
        _body = null;
        return true;
    }

    /// <summary>Call only when app callbacks are safe; the next ordered frame can be gameplay in the same poll.</summary>
    public bool TryAcceptResume(IRevisionTransitionNetwork network, IPeer peer, byte kind, long operation, long frameRevision, int payloadBytes) =>
        kind == RevisionGameplayControl.Resume && payloadBytes == 0 && Matches(peer, operation, frameRevision) &&
        _acknowledged && baseline.AppliedRevision == revision && network.GameplayRevision == revision &&
        !network.GameplayReceivingEnabled && !network.GameplaySendingEnabled && network.TrySetGameplayGates(true, true);

    private bool Matches(IPeer peer, long operation, long frameRevision) =>
        ReferenceEquals(peer, server) && operationId > 0 && revision > 0 && operation == operationId && frameRevision == revision;

    private bool Fail()
    {
        _failed = true;
        _body = null;
        return false;
    }
}
