using System.Buffers.Binary;
using System.Security.Cryptography;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Network.Core;
using Command = Karpik.Engine.Core.ModLiveTransition.Command;
using Instruction = Karpik.Engine.Core.ModLiveTransition.Instruction;
using Operation = Karpik.Engine.Core.ModLiveTransition.Operation;
using static Karpik.Engine.Shared.Network.Core.RevisionGameplayControl;

namespace Karpik.Content.Reload.HeadlessHost;

internal sealed partial class LiveProbe
{
    private const byte AbortReady = 25, AbortRestore = 26;
    private NetworkWork? _networkWork;
    private Operation _operation, _earlyCutoff, _earlyAbort, _restore;
    private ulong _settledOperation;
    private int _pairedMaximumRows;
    private RevisionDrainBarrier? _cutoff, _applied;
    private RevisionBaselineTransfer? _incoming;
    private bool _resumed;
    private long Id => checked((long)_operation.Id);
    private long Basis => checked((long)_operation.Basis);
    private long Target => checked((long)_operation.Target);

    private async ValueTask NetworkPhase(Instruction instruction, CancellationToken token)
    {
        // The main scheduler remains independently serviceable even when the client worker owns a maintenance lease.
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        Console.WriteLine($"live:phase:{instruction.Operation.Id}:{instruction.Command}:start");
        try
        {
            Task done = await _scheduler.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                return BeginPhase(instruction);
            });
            await done.WaitAsync(token).ConfigureAwait(false);
            Console.WriteLine(FormattableString.Invariant($"live:phase:{instruction.Operation.Id}:{instruction.Command}:ms:{System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F3}"));
        }
        catch (Exception error)
        {
            Console.WriteLine($"live:phase:{instruction.Operation.Id}:{instruction.Command}:error:{error.GetType().Name}:{error.Message}");
            throw;
        }
    }

    private Task BeginPhase(Instruction instruction)
    {
        var (operation, command) = instruction;
        if (command == Command.Drain)
        {
            var peer = _first;
            if (_operation.Id != 0 || peer is null || _pair?.Authorizes(peer) != true ||
                (long)operation.Basis != _net.GameplayRevision ||
                !_net.TryReserveRevisionCohort(_cohort, out int count) || count != 1)
                throw new InvalidOperationException("The complete paired cohort is not available.");
            _operation = operation;
            _cutoff = new([peer], Id, Basis, side == Side.Server ? InputCutoff : EffectCutoff);
            if (_earlyCutoff.Id == operation.Id && _earlyCutoff.Basis == operation.Basis)
                _cutoff.TryObserve(peer, _cutoff.Kind, Id, Basis, 0);
            if (side == Side.Client && !TrySendInputCutoff(_net, peer, Id, Basis)) throw new IOException("Input cutoff failed.");
        }
        else if (command == Command.Abort && _operation.Id == 0) return Task.CompletedTask;
        else if (operation != _operation) throw new InvalidOperationException("Wrong active network operation.");

        if (command == Command.Commit)
        {
            if (!_net.TryAdvanceGameplayRevision(Basis, Target)) throw new IOException("Revision advance failed.");
            if (side == Side.Client) _incoming = new(Baseline(null, Target)!, _first!, Id, Target);
            else _applied = new([_first!], Id, Target, BaselineApplied);
        }
        if (command == Command.Baseline && side == Side.Server &&
            (Baseline(_first, Target) is not { } source || !source.IsWithinReservation(CompiledHash, _pairedMaximumRows) ||
             !RevisionBaselineTransfer.TrySend(_net, _first!, Id, Target, source)))
            throw new IOException("Baseline send failed.");
        if (command == Command.Abort && side == Side.Client && _first!.ConnectionState == ConnectionState.Connected &&
            !_net.TrySendRevisionControl(_first, AbortReady, Id, Basis, []))
            throw new IOException("Abort cleanup could not be acknowledged.");

        return (_networkWork = new NetworkWork(command)).Done.Task;
    }

    private void AdvancePaired()
    {
        _pair?.ObserveLoss();
        if (_networkWork is not { } pending || pending.Done.Task.IsCompleted) return;
        try
        {
            var peer = _first!;
            bool connected = peer.ConnectionState == ConnectionState.Connected;
            if (!connected && pending.Command != Command.Abort) throw new IOException("Paired peer was lost.");
            switch (pending.Command)
            {
                case Command.Drain:
                    if (_cutoff?.AllMarkersObserved != true || !ApplicationDrained()) return;
                    _cutoff.TryConfirmApplicationDrained();
                    if (side == Side.Server && !TrySendEffectCutoff(_net, _cutoff)) throw new IOException("Effect cutoff failed.");
                    if (!_net.TrySetGameplayGates(false, false)) throw new IOException("Cannot quiesce gameplay.");
                    break;
                case Command.Baseline when side == Side.Server:
                    if (_applied?.AllMarkersObserved != true) return;
                    break;
                case Command.Baseline:
                    if (_incoming?.Complete != true) return;
                    if (!_incoming.TryApplyAndAcknowledge(_net, ClearDeferred)) throw new IOException("Baseline application failed.");
                    break;
                case Command.Resume:
                    if (side == Side.Client && !_resumed) return;
                    if (side == Side.Server && !TryResumeServer(_net, _applied!)) throw new IOException("Resume failed.");
                    EndPhase();
                    break;
                case Command.Abort:
                    if (connected)
                    {
                        if (side == Side.Server)
                        {
                            if (!_net.GameplaySendingEnabled && _earlyAbort.Id != _operation.Id) return;
                            if (!_net.TrySetGameplayGates(_net.GameplaySendingEnabled, true) ||
                                !_net.TrySendRevisionControl(peer, AbortRestore, Id, Basis, [])) throw new IOException("Abort restore failed.");
                        }
                        else if (_restore.Id != _operation.Id) return;
                    }
                    if (!_net.TrySetGameplayGates(true, true)) throw new IOException("Cannot restore old gameplay.");
                    EndPhase();
                    break;
            }
            pending.Done.TrySetResult();
        }
        catch (Exception error) { pending.Done.TrySetException(error); }
    }

    private void EndPhase()
    {
        _net.ReleaseRevisionCohort();
        _settledOperation = _operation.Id;
        _cohort[0] = null!;
        _pairedMaximumRows = 0;
        _cutoff = _applied = null; _incoming = null; _resumed = false;
        _operation = _earlyCutoff = _earlyAbort = _restore = default;
    }

    private void OnPairedControl(IPeer peer, byte kind, long id, long revision, ReadOnlySpan<byte> payload)
    {
        if (!ReferenceEquals(_first, peer) || _pair?.Authorizes(peer) != true) return;
        var fact = new Operation((ulong)id, (ulong)revision, 0);
        if (kind == (side == Side.Server ? InputCutoff : EffectCutoff) && payload.IsEmpty && revision == _net.GameplayRevision)
        {
            _earlyCutoff = fact;
            _cutoff?.TryObserve(peer, kind, id, revision, payload.Length);
        }
        if (kind == AbortReady && side == Side.Server && payload.IsEmpty && revision == _net.GameplayRevision)
            _earlyAbort = fact;
        if (id != Id) return;
        switch (kind)
        {
            case BaselineApplied: _applied?.TryObserve(peer, kind, id, revision, payload.Length); break;
            case BaselineChunk when _incoming is not null:
                if (!_incoming.TryReceiveChunk(peer, kind, id, revision, payload)) throw new InvalidDataException("Malformed paired baseline.");
                break;
            case Resume when _incoming is not null:
                _resumed = _incoming.TryAcceptResume(_net, peer, kind, id, revision, payload.Length);
                break;
            case AbortRestore when side == Side.Client && revision == Basis && payload.IsEmpty:
                // If receive had closed, the server sent Restore only after our cleanup ACK. Open before the next ordered frame.
                if (_networkWork is { Command: Command.Abort } abort && !_net.TrySetGameplayGates(true, true))
                    abort.Done.TrySetException(new IOException("Cannot restore old gameplay."));
                _restore = fact;
                break;
        }
    }

    private sealed class NetworkWork(Command command)
    {
        internal Command Command { get; } = command;
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Fixture launch capability. This proof is separate from the normal game key, private to the launch,
    // bound to both authenticated IPC generations and to fresh challenges on the actual transport peer.
    private sealed class PairSecurity(IRevisionTransitionNetwork net) : IDisposable
    {
        private const byte Challenge = 27, Proof = 28;
        private readonly byte[] _key = Convert.FromHexString(Take("KARPIK_HEADLESS_PAIR_KEY"));
        private readonly ulong _generation = ulong.Parse(Take("KARPIK_HEADLESS_PAIR_LOCAL"));
        private readonly ulong _remote = ulong.Parse(Take("KARPIK_HEADLESS_PAIR_REMOTE"));
        private readonly Guid _session = Guid.ParseExact(Take("KARPIK_HEADLESS_PAIR_SESSION"), "N");
        private readonly byte[] _ours = RandomNumberGenerator.GetBytes(32);
        private readonly byte[] _theirs = new byte[32];
        private IPeer? _peer;
        private bool _challenge;
        private readonly TaskCompletionSource _wireLoss = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task WireLoss => _wireLoss.Task;
        internal void ObserveLoss()
        {
            if (_peer is { ConnectionState: not ConnectionState.Connected }) _wireLoss.TrySetResult();
        }
        internal readonly TaskCompletionSource<bool> Authenticated = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void BindIdentity(ModLiveIpcIdentity identity)
        {
            if (identity.Session != _session || identity.Generation != _generation || _generation == _remote || _key.Length != 32)
                throw new InvalidDataException("The paired launch does not match authenticated IPC.");
            net.RevisionControlReceived += Observe;
        }
        internal void Offer(IPeer peer)
        {
            if (_peer is not null) return;
            _peer = peer;
            if (!net.TrySendRevisionControl(peer, Challenge, 1, InitialRevision, _ours)) Authenticated.TrySetResult(false);
        }
        internal bool Authorizes(IPeer peer) => ReferenceEquals(peer, _peer) &&
            peer.ConnectionState == ConnectionState.Connected && Authenticated.Task.IsCompletedSuccessfully && Authenticated.Task.Result;

        private void Observe(IPeer peer, byte kind, long id, long revision, ReadOnlySpan<byte> data)
        {
            if (!ReferenceEquals(peer, _peer) || id != 1 || revision != InitialRevision || data.Length != 32 || Authenticated.Task.IsCompleted) return;
            Span<byte> digest = stackalloc byte[32];
            if (kind == Challenge && !_challenge)
            {
                data.CopyTo(_theirs);
                _challenge = true;
                Compute(_generation, _remote, _ours, _theirs, digest);
                if (!net.TrySendRevisionControl(peer, Proof, 1, InitialRevision, digest)) Authenticated.TrySetResult(false);
            }
            else if (kind == Proof && _challenge)
            {
                Compute(_remote, _generation, _theirs, _ours, digest);
                Authenticated.TrySetResult(CryptographicOperations.FixedTimeEquals(data, digest));
            }
        }
        private void Compute(ulong sender, ulong recipient, ReadOnlySpan<byte> from, ReadOnlySpan<byte> to, Span<byte> result)
        {
            Span<byte> recipe = stackalloc byte[96];
            _session.TryWriteBytes(recipe);
            BinaryPrimitives.WriteUInt64LittleEndian(recipe[16..], sender);
            BinaryPrimitives.WriteUInt64LittleEndian(recipe[24..], recipient);
            from.CopyTo(recipe[32..64]); to.CopyTo(recipe[64..]);
            HMACSHA256.HashData(_key, recipe, result);
        }
        public void Dispose()
        {
            net.RevisionControlReceived -= Observe;
            CryptographicOperations.ZeroMemory(_key);
        }
        private static string Take(string key)
        {
            var value = Environment.GetEnvironmentVariable(key) ?? throw new InvalidDataException("Missing paired launch capability.");
            Environment.SetEnvironmentVariable(key, null);
            return value;
        }
    }
}
