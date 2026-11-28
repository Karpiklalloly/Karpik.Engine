using System.Buffers.Binary;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace Karpik.Engine.Core;

// One instance belongs to the launch owner of both sides, never inferred from a game address.
internal sealed class ModLiveLaunchOwner
{
    private readonly Guid _session = Guid.NewGuid();
    private long _lastGeneration;
    private int _pairAttached;

    internal ModLivePairedSession? TryAttachPair(ProcessManager server, ProcessManager client,
        Func<ModLiveIpcIdentity, ModLiveIpcIdentity, bool> confirmsNetworkPair, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(confirmsNetworkPair);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (server.GetModLivePeer(this) is not { Identity.Side: Side.Server } serverPeer ||
            client.GetModLivePeer(this) is not { Identity.Side: Side.Client } clientPeer ||
            Interlocked.CompareExchange(ref _pairAttached, 1, 0) != 0) return null;
        return new(serverPeer, clientPeer, confirmsNetworkPair, timeout);
    }

    internal ModLiveLaunchAssociation CreateAssociation(Side side) => new(
        new(_session, checked((ulong)Interlocked.Increment(ref _lastGeneration)), side),
        RandomNumberGenerator.GetBytes(ModLiveLaunchAssociation.SecretSize));
}

internal readonly record struct ModLiveIpcIdentity(Guid Session, ulong Generation, Side Side);

// Per-start capability. It is not a command-line argument and is never sent on the pipe.
internal sealed class ModLiveLaunchAssociation(ModLiveIpcIdentity identity, byte[] secret) : IDisposable
{
    internal const string EnvironmentKey = "KARPIK_MOD_LIVE_ASSOCIATION";
    internal const int SecretSize = 32;
    private const int NonceSize = 32;
    private byte[]? _secret = secret;
    internal ModLiveIpcIdentity Identity { get; } = identity;

    internal string ToLaunchValue()
    {
        byte[] key = _secret ?? throw new InvalidOperationException("Mod-live association already consumed.");
        return FormattableString.Invariant(
            $"{Identity.Session:N}:{Identity.Generation:X16}:{(byte)Identity.Side:X2}:{Convert.ToHexString(key)}");
    }

    // WorkerHost must take this at startup, before it can launch any descendants.
    internal static ModLiveLaunchAssociation? TakeFromEnvironment()
    {
        string? value = Environment.GetEnvironmentVariable(EnvironmentKey);
        if (value is null) return null;
        Environment.SetEnvironmentVariable(EnvironmentKey, null);
        return Parse(value);
    }

    internal static ModLiveLaunchAssociation Parse(string value)
    {
        if (value.Length != 117 || value[32] != ':' || value[49] != ':' || value[52] != ':' ||
            !Guid.TryParseExact(value.AsSpan(0, 32), "N", out Guid session) || session == Guid.Empty ||
            !ulong.TryParse(value.AsSpan(33, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong generation) ||
            generation == 0 ||
            !byte.TryParse(value.AsSpan(50, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte side) ||
            (Side)side is not (Side.Client or Side.Server))
        {
            throw new InvalidDataException("Invalid mod-live launch association.");
        }

        var secret = new byte[SecretSize];
        if (Convert.FromHexString(value.AsSpan(53), secret, out _, out int written) != System.Buffers.OperationStatus.Done ||
            written != SecretSize)
        {
            CryptographicOperations.ZeroMemory(secret);
            throw new InvalidDataException("Invalid mod-live launch association.");
        }
        return new(new(session, generation, (Side)side), secret);
    }

    internal async Task AuthenticateAsync(
        PipeStream pipe, bool isOwner, Func<IpcMessage, CancellationToken, Task> send, CancellationToken token)
    {
        byte[] key = Interlocked.Exchange(ref _secret, null)
            ?? throw new InvalidOperationException("Mod-live association already consumed.");
        try
        {
            if (isOwner)
            {
                byte[] challenge = RandomNumberGenerator.GetBytes(NonceSize);
                await send(new(ModLiveIpcProtocol.Challenge, challenge), token).ConfigureAwait(false);
                byte[] response = await ReadProofAsync(pipe, ModLiveIpcProtocol.Response, token).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(
                        Proof(key, ModLiveIpcProtocol.Response, challenge, response), response.AsSpan(NonceSize)))
                    throw new InvalidDataException("Mod-live association rejected.");
                await send(new(ModLiveIpcProtocol.Confirm, Proof(key, ModLiveIpcProtocol.Confirm, challenge, response)), token)
                    .ConfigureAwait(false);
            }
            else
            {
                byte[] challenge = await ReadProofAsync(pipe, ModLiveIpcProtocol.Challenge, token).ConfigureAwait(false);
                var response = new byte[NonceSize + SecretSize];
                RandomNumberGenerator.Fill(response.AsSpan(0, NonceSize));
                Proof(key, ModLiveIpcProtocol.Response, challenge, response).CopyTo(response, NonceSize);
                await send(new(ModLiveIpcProtocol.Response, response), token).ConfigureAwait(false);
                byte[] confirm = await ReadProofAsync(pipe, ModLiveIpcProtocol.Confirm, token).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(Proof(key, ModLiveIpcProtocol.Confirm, challenge, response), confirm))
                    throw new InvalidDataException("Mod-live association rejected.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _secret, null) is { } key) CryptographicOperations.ZeroMemory(key);
    }

    private byte[] Proof(byte[] key, IpcMessageType direction, ReadOnlySpan<byte> challenge, ReadOnlySpan<byte> response)
    {
        Span<byte> input = stackalloc byte[26 + 2 * NonceSize];
        input[0] = (byte)direction;
        Identity.Session.TryWriteBytes(input[1..17]);
        BinaryPrimitives.WriteUInt64LittleEndian(input[17..25], Identity.Generation);
        input[25] = (byte)Identity.Side;
        challenge.CopyTo(input[26..]);
        response[..NonceSize].CopyTo(input[(26 + NonceSize)..]);
        return HMACSHA256.HashData(key, input);
    }

    private static async Task<byte[]> ReadProofAsync(PipeStream pipe, IpcMessageType type, CancellationToken token)
    {
        IpcMessage? message = await IpcFrame.ReadAsync(pipe, new byte[5], token, type).ConfigureAwait(false);
        if (message is not { } received) throw new EndOfStreamException();
        return received.Payload;
    }
}

internal enum ModLiveIpcKind : byte
{
    Discover = 1, Discovered, Select, Drain, Drained, Prepare, Ready, Rejected, Commit, Committed,
    Baseline, BaselineApplied, Abort, Resume, FailStop, Aborted, Resumed
}

internal readonly record struct ModLiveIpcMessage(
    ModLiveIpcKind Kind, ModLiveTransition.Operation Operation, ReadOnlyMemory<byte> Payload = default);

internal static class ModLiveIpcProtocol
{
    internal const IpcMessageType Control = (IpcMessageType)0x60;
    internal const IpcMessageType Challenge = (IpcMessageType)0x61;
    internal const IpcMessageType Response = (IpcMessageType)0x62;
    internal const IpcMessageType Confirm = (IpcMessageType)0x63;
    internal const int HeaderSize = 25;
    internal const ulong InitialRevision = 1;
    internal const int MaxPayload = 32 * 1024;

    internal static bool IsReserved(IpcMessageType type) => type is Control or Challenge or Response or Confirm;

    internal static bool HasValidLength(IpcMessageType type, int length) => type switch
    {
        Control => length is >= HeaderSize and <= HeaderSize + MaxPayload,
        Challenge or Confirm => length == 32,
        Response => length == 64,
        _ => length is >= 0 and <= int.MaxValue - 5
    };

    internal static IpcMessage Encode(ModLiveIpcMessage message, bool fromOwner)
    {
        if (!IsValid(message, fromOwner)) throw new ArgumentException("Invalid mod-live control message.", nameof(message));
        var data = new byte[HeaderSize + message.Payload.Length];
        data[0] = (byte)message.Kind;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(1), message.Operation.Id);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(9), message.Operation.Basis);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(17), message.Operation.Target);
        message.Payload.Span.CopyTo(data.AsSpan(HeaderSize));
        return new(Control, data);
    }

    internal static bool TryDecode(IpcMessage frame, bool fromOwner, out ModLiveIpcMessage message)
    {
        message = default;
        if (frame.Type != Control || !HasValidLength(frame.Type, frame.Payload.Length)) return false;
        var data = frame.Payload.AsSpan();
        message = new((ModLiveIpcKind)data[0], new(
            BinaryPrimitives.ReadUInt64LittleEndian(data[1..]), BinaryPrimitives.ReadUInt64LittleEndian(data[9..]),
            BinaryPrimitives.ReadUInt64LittleEndian(data[17..])), frame.Payload.AsMemory(HeaderSize));
        return IsValid(message, fromOwner);
    }

    private static bool IsValid(ModLiveIpcMessage message, bool fromOwner)
    {
        bool validDirection = message.Kind switch
        {
            ModLiveIpcKind.Discover or ModLiveIpcKind.Select or ModLiveIpcKind.Drain or ModLiveIpcKind.Prepare or
                ModLiveIpcKind.Commit or ModLiveIpcKind.Baseline or ModLiveIpcKind.Abort or ModLiveIpcKind.Resume or
                ModLiveIpcKind.FailStop => fromOwner,
            ModLiveIpcKind.Discovered or ModLiveIpcKind.Drained or ModLiveIpcKind.Ready or ModLiveIpcKind.Rejected or
                ModLiveIpcKind.Committed or ModLiveIpcKind.BaselineApplied or ModLiveIpcKind.Aborted or ModLiveIpcKind.Resumed => !fromOwner,
            _ => false
        };
        if (!validDirection || message.Operation.Id == 0 || message.Payload.Length > MaxPayload) return false;
        bool discovery = message.Kind is ModLiveIpcKind.Discover or ModLiveIpcKind.Discovered;
        if (discovery ? message.Operation.Target != message.Operation.Basis :
            message.Operation.Target < message.Operation.Basis ||
            (message.Operation.Target == message.Operation.Basis && message.Kind != ModLiveIpcKind.Rejected)) return false;
        if (message.Kind == ModLiveIpcKind.Ready) return message.Payload.Length == 32;
        // Manifest/selection/diagnostic semantics belong to the owner, not the pipe decoder.
        return message.Payload.IsEmpty || message.Kind is ModLiveIpcKind.Discovered or ModLiveIpcKind.Select or
            ModLiveIpcKind.Prepare or ModLiveIpcKind.Rejected;
    }
}

// One pipe, one exact generation. The serialized owner drains the inbox, not the listener thread.
internal sealed class ModLiveIpcPeer(
    ModLiveIpcIdentity identity, bool isOwner, Func<IpcMessage, CancellationToken, Task> send, Action disconnect)
{
    internal const int InboxCapacity = 8;
    private readonly object _gate = new();
    private readonly Channel<ModLiveIpcMessage> _inbox = Channel.CreateBounded<ModLiveIpcMessage>(InboxCapacity);
    private readonly TaskCompletionSource _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _sending;

    internal ModLiveIpcIdentity Identity { get; } = identity;
    internal Task Loss => _lost.Task;
    internal object InboxGate => _gate;

    internal ValueTask<bool> WaitToReadAsync(CancellationToken token) => _inbox.Reader.WaitToReadAsync(token);

    internal bool TryRead(out ModLiveIpcMessage message)
    {
        lock (_gate) return _inbox.Reader.TryRead(out message);
    }

    internal bool TryReceive(IpcMessage frame)
    {
        lock (_gate)
        {
            if (Loss.IsCompleted) return false;
            if (ModLiveIpcProtocol.TryDecode(frame, !isOwner, out var message) && _inbox.Writer.TryWrite(message)) return true;
            Close();
            return false;
        }
    }

    internal async Task SendAsync(ModLiveIpcMessage message, CancellationToken token)
    {
        if (Loss.IsCompleted) throw new InvalidOperationException("Mod-live worker generation is no longer available.");
        IpcMessage frame = ModLiveIpcProtocol.Encode(message, isOwner);
        if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0)
            throw new InvalidOperationException("A mod-live control write is already pending.");
        try
        {
            await send(frame, token).ConfigureAwait(false);
        }
        catch
        {
            Close();
            disconnect(); // A failed write may have left a partial frame in the stream.
            throw;
        }
        finally
        {
            Volatile.Write(ref _sending, 0);
        }
    }

    internal void Disconnect()
    {
        Close();
        disconnect();
    }

    internal void Close()
    {
        lock (_gate)
        {
            while (_inbox.Reader.TryRead(out _)) { }
            _lost.TrySetResult();
            _inbox.Writer.TryComplete();
        }
    }
}
