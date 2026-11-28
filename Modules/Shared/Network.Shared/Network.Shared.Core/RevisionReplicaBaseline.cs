using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.InteropServices;
using DCFApixels.DragonECS;

namespace Karpik.Engine.Shared.Network.Core;

/// <summary>Prepared fixed-width projection for one revision. Validate must consume exactly the bytes Apply consumes, without touching the world.</summary>
/// <remarks>The owner binds the explicitly projected fields before the barrier. Report invalid input with InvalidDataException. Apply owns field presence, not the entity.</remarks>
public interface IRevisionReplicaProjection
{
    long SchemaHash { get; }
    /// <summary>Full encoded row with all presence bits and fields; unknown projections cannot enter live transitions.</summary>
    int? MaximumRowBytes => null;
    void Write(EcsWorld world, int entity, IWriter writer);
    void Validate(IReader reader);
    void Apply(EcsWorld world, int entity, IReader reader);
}

public readonly record struct RevisionReplicaIdentity(int Entity, int Id, long Incarnation);

/// <summary>The authoritative per-peer visibility before commit and a hard bound on additions through activation and baseline capture.</summary>
/// <remarks>Compute on the quiescent owner. Unknown or unenforceable growth must be null, not zero.</remarks>
public readonly record struct RevisionBaselineBudget(EcsWorld World, ReadOnlyMemory<RevisionReplicaIdentity> Visible,
    int? MaximumAdditionalRows)
{
    public bool TryReserve(IRevisionReplicaProjection compiled, int? stagedModRowBytes, out int maximumRows)
    {
        maximumRows = 0;
        int? compiledRowBytes = compiled.MaximumRowBytes;
        if (World is null || MaximumAdditionalRows is not >= 0 || compiledRowBytes is not >= 0 || stagedModRowBytes is not >= 0)
            return false;
        long rows = (long)Visible.Length + MaximumAdditionalRows.Value;
        long rowBytes = sizeof(int) + sizeof(long) + (long)compiledRowBytes.Value + stagedModRowBytes.Value;
        if (rows > RevisionReplicaBaseline.MaxReplicas || RevisionReplicaBaseline.HeaderBytes + rows * rowBytes > RevisionReplicaBaseline.MaxBytes)
            return false;

        var ids = new HashSet<int>(Visible.Length);
        var entities = new HashSet<int>(Visible.Length);
        foreach (var row in Visible.Span)
            if (!RevisionReplicaBaseline.ValidIdentity(row.Id, row.Incarnation) || row.Entity < 0 || row.Entity >= World.Capacity ||
                !World.IsUsed(row.Entity) || !ids.Add(row.Id) || !entities.Add(row.Entity)) return false;
        maximumRows = (int)rows;
        return true;
    }
}

/// <summary>Explicit client ownership. A legacy NetworkId alone does not make an entity part of this stream.</summary>
public struct NetworkReplica : IEcsComponent
{
    public long Stream;
    public int Id;
    public long Incarnation;
}

/// <summary>Cold, bounded full baseline for a single stream and prepared projection. Call with ECS jobs quiescent.</summary>
public sealed class RevisionReplicaBaseline
{
    public const int MaxBytes = 64 * 1024;
    public const int MaxReplicas = 1024;
    internal const int HeaderBytes = 4 * sizeof(long) + sizeof(int);

    private readonly EcsWorld _world;
    private readonly long _stream;
    private readonly IRevisionReplicaProjection _compiled;
    private readonly IRevisionReplicaProjection? _mod;
    private readonly long _compiledHash;
    private readonly long _modHash;
    private readonly BaselineBuffer _buffer = new();
    private readonly Dictionary<int, long> _desired = new(MaxReplicas);
    private readonly Dictionary<int, (long Incarnation, int Entity, short Generation)> _existing = new(MaxReplicas);
    private readonly Dictionary<int, (long Incarnation, entlong Entity)> _applied = new(MaxReplicas);

    public RevisionReplicaBaseline(EcsWorld world, long stream, IRevisionReplicaProjection compiled, IRevisionReplicaProjection? mod = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stream);
        _world = world;
        _stream = stream;
        _compiled = compiled;
        _mod = mod;
        _compiledHash = compiled.SchemaHash;
        _modHash = mod?.SchemaHash ?? 0;
        if (_compiledHash == 0 || (mod is not null && _modHash == 0))
            throw new ArgumentException("A prepared projection requires a nonzero schema hash.");
    }

    /// <summary>Check the current owned stream before Ready, with ECS jobs quiescent. Does not create pools or change replicas.</summary>
    public bool CanReconcileExistingStream() => IndexExisting();

    public ReadOnlyMemory<byte> Encoded { get; private set; }
    public long AppliedRevision { get; private set; }

    public bool IsWithinReservation(long compiledHash, int maximumRows) => _compiledHash == compiledHash &&
        Encoded.Length >= HeaderBytes && BinaryPrimitives.ReadInt32LittleEndian(Encoded.Span[(HeaderBytes - sizeof(int))..]) <= maximumRows;

    /// <summary>Capture exactly the caller's authoritative visibility set; an empty set is valid. Encoded lasts until the next capture.</summary>
    public bool TryCapture(ReadOnlySpan<RevisionReplicaIdentity> visible, long revision)
    {
        Encoded = default;
        if (revision <= 0 || visible.Length > MaxReplicas)
            return false;

        _desired.Clear();
        _buffer.Reset();
        try
        {
            _buffer.Put(revision);
            _buffer.Put(_stream);
            _buffer.Put(_compiledHash);
            _buffer.Put(_modHash);
            _buffer.Put(visible.Length);
            foreach (var row in visible)
            {
                if (!ValidIdentity(row.Id, row.Incarnation) || !_desired.TryAdd(row.Id, row.Incarnation) ||
                    row.Entity < 0 || row.Entity >= _world.Capacity || !_world.IsUsed(row.Entity))
                    return false;
                _buffer.Put(row.Id);
                _buffer.Put(row.Incarnation);
                _compiled.Write(_world, row.Entity, _buffer);
                _mod?.Write(_world, row.Entity, _buffer);
            }
            Encoded = _buffer.Written;
            return true;
        }
        catch (InvalidDataException) { return false; }
    }

    /// <summary>Reject malformed/stale baselines without changing the live world or previously-applied index.</summary>
    public bool TryApply(ReadOnlyMemory<byte> data, long expectedRevision)
    {
        if (expectedRevision <= AppliedRevision || data.Length is < HeaderBytes or > MaxBytes)
            return false;

        _buffer.Bind(data);
        try
        {
            if (!Validate(expectedRevision, out int count) || !IndexExisting())
                return false;
            foreach (var (id, incarnation) in _desired)
                if (_existing.TryGetValue(id, out var previous) && incarnation < previous.Incarnation)
                    return false;

            // No world changes above this point. An exception from the prepared adapter during publication is a host fail-stop.
            Reconcile(count);
            AppliedRevision = expectedRevision;
            return true;
        }
        finally { _buffer.Bind(default); }
    }

    private bool Validate(long expectedRevision, out int count)
    {
        count = 0;
        try
        {
            if (_buffer.GetLong() != expectedRevision || _buffer.GetLong() != _stream ||
                _buffer.GetLong() != _compiledHash || _buffer.GetLong() != _modHash)
                return false;
            count = _buffer.GetInt();
            if ((uint)count > MaxReplicas)
                return false;

            _desired.Clear();
            for (int i = 0; i < count; i++)
            {
                int id = _buffer.GetInt();
                long incarnation = _buffer.GetLong();
                if (!ValidIdentity(id, incarnation) || !_desired.TryAdd(id, incarnation))
                    return false;
                _compiled.Validate(_buffer);
                _mod?.Validate(_buffer);
            }
            return _buffer.AvailableBytes == 0;
        }
        catch (InvalidDataException) { return false; }
    }

    private bool IndexExisting()
    {
        _existing.Clear();
        // The Type lookup declares a world component ID if called on an undeclared type.
        if (!_world.IsComponentTypeDeclared<NetworkReplica>() || !_world.TryFindPoolInstance(typeof(NetworkReplica), out var pool))
            return true;
        if (pool is not EcsPool<NetworkReplica> replicas) return false;
        if (replicas.Count == 0) return true;
        // ponytail: O(world capacity) on the cold path; use a read-only pool iterator if sparse worlds make this costly.
        // Queries may play pending deletes, and Get/packing an entlong can run listeners or wake generations.
        for (int entity = 0; entity < _world.Capacity; entity++)
        {
            if (!_world.IsUsed(entity) || !replicas.Has(entity)) continue;
            ref readonly var replica = ref replicas.Read(entity);
            if (replica.Stream != _stream)
                continue;
            if (!ValidIdentity(replica.Id, replica.Incarnation) || _existing.Count >= MaxReplicas ||
                !_existing.TryAdd(replica.Id, (replica.Incarnation, entity, default)))
                return false;
        }
        return true;
    }

    private void Reconcile(int count)
    {
        // Only after validation: pin every existing generation before any deletion invokes listeners.
        EcsPool<NetworkReplica>? replicas = _existing.Count == 0 ? null : _world.GetPool<NetworkReplica>();
        foreach (var (id, _) in _existing)
        {
            ref var previous = ref CollectionsMarshal.GetValueRefOrNullRef(_existing, id);
            previous.Generation = _world.GetGen(previous.Entity);
        }

        bool removed = false;
        foreach (var (id, previous) in _existing)
        {
            if (_desired.TryGetValue(id, out var incarnation) && incarnation == previous.Incarnation)
                continue;
            if (StillOwns(replicas!, previous.Entity, previous.Generation, id, previous.Incarnation))
                removed |= _world.TryDelEntity(previous.Entity);
        }
        if (removed)
            _world.ReleaseDelEntityBufferAll();

        _applied.Clear();
        _buffer.Rewind(HeaderBytes);
        for (int i = 0; i < count; i++)
        {
            int id = _buffer.GetInt();
            long incarnation = _buffer.GetLong();
            int entity;
            short generation;
            if (_existing.TryGetValue(id, out var previous) && previous.Incarnation == incarnation &&
                StillOwns(replicas!, previous.Entity, previous.Generation, id, incarnation))
            {
                (entity, generation) = (previous.Entity, previous.Generation);
            }
            else
            {
                replicas ??= _world.GetPool<NetworkReplica>();
                entity = _world.NewEntity();
                generation = _world.GetGen(entity);
                replicas.Add(entity) = new NetworkReplica { Stream = _stream, Id = id, Incarnation = incarnation };
                RequireOwned();
            }
            _compiled.Apply(_world, entity, _buffer);
            if (_mod is not null)
            {
                RequireOwned();
                _mod.Apply(_world, entity, _buffer);
            }
            RequireOwned();
            _applied.Add(id, (incarnation, _world.GetEntityLong(entity)));

            void RequireOwned()
            {
                if (!StillOwns(replicas!, entity, generation, id, incarnation))
                    throw new InvalidOperationException("A baseline publication callback replaced the active replica.");
            }
        }
        if (_buffer.AvailableBytes != 0)
            throw new InvalidOperationException("Prepared projection validation and application consumed different bytes.");
    }

    private bool StillOwns(EcsPool<NetworkReplica> replicas, int entity, short generation, int id, long incarnation)
    {
        if (!_world.IsAliveSafe(entity, generation) || !replicas.Has(entity)) return false;
        ref readonly var replica = ref replicas.Read(entity);
        return replica.Stream == _stream && replica.Id == id && replica.Incarnation == incarnation;
    }

    public bool TryFind(int id, long incarnation, out int entity)
    {
        if (_applied.TryGetValue(id, out var entry) && entry.Incarnation == incarnation)
            return entry.Entity.TryUnpack(_world, out entity);
        entity = default;
        return false;
    }

    internal static bool ValidIdentity(int id, long incarnation) => id >= 0 && incarnation > 0;

    // The cold baseline reuses the existing typed codec interface. Only the capture side needs a byte slab.
    private sealed class BaselineBuffer : IWriter, IReader
    {
        private byte[]? _output;
        private ReadOnlyMemory<byte> _input;
        private int _position;
        public ReadOnlyMemory<byte> Written => _output.AsMemory(0, _position);
        public int AvailableBytes => _input.Length - _position;
        public void Reset() { _output ??= new byte[MaxBytes]; _position = 0; }
        public void Bind(ReadOnlyMemory<byte> input) { _input = input; _position = 0; }
        public void Rewind(int position) => _position = position;
        public void Recycle() { }

        private ReadOnlySpan<byte> Read(int length)
        {
            if (length > AvailableBytes) throw new InvalidDataException("Truncated baseline.");
            var data = _input.Span.Slice(_position, length);
            _position += length;
            return data;
        }

        private Span<byte> Write(int length)
        {
            if (length > MaxBytes - _position) throw new InvalidDataException("Baseline byte budget exceeded.");
            var data = _output.AsSpan(_position, length);
            _position += length;
            return data;
        }

        public void Put(byte value) => Write(1)[0] = value;
        public void Put(bool value) => Put(value ? (byte)1 : (byte)0);
        public void Put(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Write(2), value);
        public void Put(int value) => BinaryPrimitives.WriteInt32LittleEndian(Write(4), value);
        public void Put(long value) => BinaryPrimitives.WriteInt64LittleEndian(Write(8), value);
        public void Put(float value) => Put(BitConverter.SingleToInt32Bits(value));
        public void Put(double value) => Put(BitConverter.DoubleToInt64Bits(value));
        public void Put(Color value) => Put(value.ToArgb());
        public void Put(string value) => throw new InvalidDataException("Baseline fields must be fixed-width.");
        public byte GetByte() => Read(1)[0];
        public bool GetBool() => GetByte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid baseline boolean.") };
        public ushort GetUShort() => BinaryPrimitives.ReadUInt16LittleEndian(Read(2));
        public int GetInt() => BinaryPrimitives.ReadInt32LittleEndian(Read(4));
        public long GetLong() => BinaryPrimitives.ReadInt64LittleEndian(Read(8));
        public float GetFloat() => BitConverter.Int32BitsToSingle(GetInt());
        public double GetDouble() => BitConverter.Int64BitsToDouble(GetLong());
        public Color GetColor() => Color.FromArgb(GetInt());
        public string GetString() => throw new InvalidDataException("Baseline fields must be fixed-width.");
    }
}
