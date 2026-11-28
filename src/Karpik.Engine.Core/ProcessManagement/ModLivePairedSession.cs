using System.Buffers.Binary;
using System.Text;
using Command = Karpik.Engine.Core.ModLiveTransition.Command;
using Fact = Karpik.Engine.Core.ModLiveTransition.Fact;
using Kind = Karpik.Engine.Core.ModLiveIpcKind;
using Operation = Karpik.Engine.Core.ModLiveTransition.Operation;
using Status = Karpik.Engine.Core.SimulationCodeReloadStatus;

namespace Karpik.Engine.Core;

// A fixed, explicitly paired launch. Only one cold owner pump may consume its two inboxes.
internal sealed class ModLivePairedSession(
    ModLiveIpcPeer server, ModLiveIpcPeer client,
    Func<ModLiveIpcIdentity, ModLiveIpcIdentity, bool> confirmsNetworkPair, TimeSpan timeout)
{
    private readonly ModLiveIpcPeer[] _peers = [server, client];
    private readonly ModLiveTransition _transition = new(ModLiveIpcProtocol.InitialRevision);
    private ulong _lastId;
    private int _busy;
    private bool _failed;

    private bool IsPaired => server.Identity.Side == Side.Server && client.Identity.Side == Side.Client &&
        server.Identity.Session != Guid.Empty && server.Identity.Session == client.Identity.Session &&
        server.Identity.Generation != client.Identity.Generation &&
        !server.Loss.IsCompleted && !client.Loss.IsCompleted && confirmsNetworkPair(server.Identity, client.Identity);

    internal async Task<(string[] Server, string[] Client)?> DiscoverAsync(CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return null;
        try
        {
            if (_failed || !IsPaired || !_transition.TryCaptureAcceptedRevision(out ulong revision)) return null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);
            var operation = new Operation(++_lastId, revision, revision);
            await Broadcast(new(Kind.Discover, operation));
            var discovered = new string[2][];
            byte remaining = 3;
            while (remaining != 0)
            {
                var next = await Read(operation, remaining, deadline.Token, watchWithdrawal: true);
                if (next.Message.Kind != Kind.Discovered || !ModLiveIpcSelection.TryDecode(next.Message.Payload.Span, out var ids))
                    return null;
                discovered[next.Index] = ids;
                remaining ^= (byte)(1 << next.Index);
            }
            return (discovered[0], discovered[1]);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException) { return null; }
        finally { Volatile.Write(ref _busy, 0); }
    }

    internal async Task<Status> ApplyAsync(IReadOnlyCollection<string> selectedIds, CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return Status.Busy;
        try
        {
            if (_failed) return Status.Faulted;
            if (!IsPaired) return Status.Unsupported;
            if (token.IsCancellationRequested) return Status.Cancelled;
            if (!_transition.TryCaptureAcceptedRevision(out ulong revision) || revision == ulong.MaxValue ||
                !ModLiveIpcSelection.TryEncode(selectedIds, out var selection)) return Status.Rejected;
            var operation = new Operation(++_lastId, revision, revision + 1);
            if (_transition.TryBegin(operation, [new(server.Identity.Generation, false), new(client.Identity.Generation, true)])
                != ModLiveTransition.Admission.Started) return Status.InvalidState;

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);
            ReadOnlyMemory<byte> fingerprint = default;
            bool commitPending = false;
            while (true)
            {
                // Notice loss/cancellation before taking a pending Commit instruction (the decision).
                if (deadline.IsCancellationRequested || server.Loss.IsCompleted || client.Loss.IsCompleted)
                    _transition.Interrupt(operation, ModLiveTransition.Interruption.Stop);
                var command = commitPending ? DecideCommit(operation, deadline.Token) : _transition.Step().Command;
                commitPending = false;
                if (command == Command.FailStop) return await FailStop(operation);
                if (command == Command.Abort)
                {
                    using var recovery = new CancellationTokenSource(timeout);
                    await Broadcast(new(Kind.Abort, operation));
                    try { await WaitSettlement(operation, Kind.Aborted, 3, recovery.Token); }
                    catch (Exception e) when (e is OperationCanceledException or IOException) { return await FailStop(operation); }
                    _transition.Complete(operation, Command.Abort);
                    return token.IsCancellationRequested ? Status.Cancelled : Status.Rejected;
                }

                try
                {
                    if (command == Command.Resume)
                    {
                        // Server network admission/Resume marker is handed off before releasing the client.
                        await Send(server, new(Kind.Resume, operation));
                        await WaitSettlement(operation, Kind.Resumed, 1, deadline.Token);
                        await Send(client, new(Kind.Resume, operation));
                        await WaitSettlement(operation, Kind.Resumed, 2, deadline.Token);
                        if (!IsPaired || deadline.IsCancellationRequested)
                            throw new IOException("A paired worker was lost during Resume.");
                        _transition.Complete(operation, Command.Resume);
                        return Status.Success;
                    }

                    (Kind request, Kind expected, Fact fact, byte remaining) = command switch
                    {
                        Command.Drain => (Kind.Drain, Kind.Drained, Fact.Drained, (byte)3),
                        Command.Prepare => (Kind.Prepare, Kind.Ready, Fact.Ready, (byte)3),
                        Command.Commit => (Kind.Commit, Kind.Committed, Fact.Committed, (byte)3),
                        Command.Baseline => (Kind.Baseline, Kind.BaselineApplied, Fact.BaselineApplied, (byte)3),
                        _ => throw new InvalidOperationException("No phase available for the paired owner.")
                    };
                    if (command == Command.Prepare) await Broadcast(new(Kind.Select, operation, selection));
                    await Broadcast(new(request, operation));
                    while (remaining != 0)
                    {
                        var next = await Read(operation, remaining, deadline.Token, watchWithdrawal: true);
                        ulong participant = _peers[next.Index].Identity.Generation;
                        if (next.Message.Kind == Kind.Rejected)
                        {
                            _transition.Interrupt(operation, ModLiveTransition.Interruption.Stop);
                            break;
                        }
                        if (next.Message.Kind != expected ||
                            (!(command == Command.Baseline && next.Index == 0) && !_transition.Receive(operation, participant, fact))) continue;
                        if (expected == Kind.Ready)
                        {
                            if (fingerprint.IsEmpty) fingerprint = next.Message.Payload;
                            else if (!fingerprint.Span.SequenceEqual(next.Message.Payload.Span) || !IsPaired)
                            {
                                _transition.Interrupt(operation, ModLiveTransition.Interruption.Stop);
                                break;
                            }
                        }
                        remaining ^= (byte)(1 << next.Index);
                    }
                    commitPending = command == Command.Prepare && remaining == 0;
                }
                catch (Exception e) when (e is OperationCanceledException or IOException)
                {
                    _transition.Interrupt(operation, ModLiveTransition.Interruption.Stop);
                }
            }
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private Command DecideCommit(Operation operation, CancellationToken token)
    {
        // Admission of control frames and taking Commit have a single order. No IO or await under these cold gates.
        lock (server.InboxGate)
        lock (client.InboxGate)
        {
            if (token.IsCancellationRequested || server.Loss.IsCompleted || client.Loss.IsCompleted ||
                Withdrawn(server) || Withdrawn(client))
                _transition.Interrupt(operation, ModLiveTransition.Interruption.Stop);
            return _transition.Step().Command;
        }

        bool Withdrawn(ModLiveIpcPeer peer)
        {
            while (peer.TryRead(out var message))
                if (message.Operation == operation && message.Kind == Kind.Rejected) return true;
            return false;
        }
    }

    private async Task<Status> FailStop(Operation operation)
    {
        _failed = true;
        try { await Broadcast(new(Kind.FailStop, operation)); }
        finally
        {
            server.Disconnect();
            client.Disconnect();
        }
        return Status.Faulted;
    }

    private async Task WaitSettlement(Operation operation, Kind expected, byte remaining, CancellationToken token)
    {
        while (remaining != 0)
        {
            var next = await Read(operation, remaining, token, ignoreLoss: expected == Kind.Aborted);
            if (next.Index < 0) return;
            // A peer can have queued a Prepare rejection before seeing Abort; only its ACK settles cleanup.
            if (expected != Kind.Aborted && next.Message.Kind == Kind.Rejected)
                throw new IOException("A worker could not settle the mod-live operation.");
            if (next.Message.Kind == expected) remaining ^= (byte)(1 << next.Index);
        }
    }

    private async Task<(int Index, ModLiveIpcMessage Message)> Read(Operation operation, byte remaining,
        CancellationToken token, bool ignoreLoss = false, bool watchWithdrawal = false)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!ignoreLoss && (server.Loss.IsCompleted || client.Loss.IsCompleted))
                throw new IOException("A paired worker was lost.");
            for (int index = 0; index < _peers.Length; index++)
            {
                bool needsReply = (remaining & (1 << index)) != 0;
                if (!needsReply && !watchWithdrawal) continue;
                var peer = _peers[index];
                if (peer.Loss.IsCompleted) { if (needsReply) remaining ^= (byte)(1 << index); continue; }
                while (peer.TryRead(out var message))
                {
                    if (message.Operation == operation && (needsReply || message.Kind == Kind.Rejected)) return (index, message);
                    token.ThrowIfCancellationRequested();
                }
            }
            if (remaining == 0) return (-1, default);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task a = watchWithdrawal || (remaining & 1) != 0 ? server.WaitToReadAsync(wait.Token).AsTask() : client.Loss;
            Task b = watchWithdrawal || (remaining & 2) != 0 ? client.WaitToReadAsync(wait.Token).AsTask() : server.Loss;
            if (ignoreLoss) await Task.WhenAny(a, b).WaitAsync(token).ConfigureAwait(false);
            else await Task.WhenAny(a, b, server.Loss, client.Loss).WaitAsync(token).ConfigureAwait(false);
            wait.Cancel();
        }
    }

    private Task Broadcast(ModLiveIpcMessage message) => Task.WhenAll(Send(server, message), Send(client, message));

    private async Task Send(ModLiveIpcPeer peer, ModLiveIpcMessage message)
    {
        if (peer.Loss.IsCompleted) return;
        using var io = new CancellationTokenSource(timeout);
        try { await peer.SendAsync(message, io.Token).ConfigureAwait(false); }
        catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException)
        {
            // SendAsync revokes the generation if writing cannot be completed.
            if (!peer.Loss.IsCompleted) throw;
        }
    }
}

internal static class ModLiveIpcSelection
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const int MaxIds = 256, MaxIdBytes = 128;

    internal static bool TryEncode(IReadOnlyCollection<string> ids, out byte[] payload)
    {
        payload = [];
        if (ids.Count > MaxIds) return false;
        using var stream = new MemoryStream();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        Span<byte> entry = stackalloc byte[2 + MaxIdBytes];
        try
        {
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id) || id.Length > MaxIdBytes || !unique.Add(id) || unique.Count > MaxIds) return false;
                int size = Utf8.GetByteCount(id);
                if (size > MaxIdBytes || stream.Length + 2 + size > ModLiveIpcProtocol.MaxPayload) return false;
                BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)size);
                Utf8.GetBytes(id, entry[2..]);
                stream.Write(entry[..(2 + size)]);
            }
        }
        catch (EncoderFallbackException) { return false; }
        payload = stream.ToArray();
        return true;
    }

    internal static bool TryDecode(ReadOnlySpan<byte> payload, out string[] ids)
    {
        ids = [];
        if (payload.Length > ModLiveIpcProtocol.MaxPayload) return false;
        var list = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            while (!payload.IsEmpty)
            {
                if (payload.Length < 2 || list.Count >= MaxIds) return false;
                int size = BinaryPrimitives.ReadUInt16LittleEndian(payload);
                if (size == 0 || size > MaxIdBytes || size > payload.Length - 2) return false;
                string id = Utf8.GetString(payload.Slice(2, size));
                if (string.IsNullOrWhiteSpace(id) || !unique.Add(id)) return false;
                list.Add(id);
                payload = payload[(size + 2)..];
            }
        }
        catch (DecoderFallbackException) { return false; }
        ids = list.ToArray();
        return true;
    }
}
