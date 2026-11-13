using DCFApixels.DragonECS;
using System.Runtime.CompilerServices;

namespace Karpik.Engine.Shared.Modding.Bindings;

/// <summary>Host limits reserved once for sequential callbacks in one Simulation.</summary>
public sealed record StructuralCommandOptions
{
    public int MaxCommands { get; init; } = 1024;
    public int MaxPendingEntities { get; init; } = 256;
    public int MaxPayloadBytes { get; init; } = 256 * 1024;
}

/// <summary>An immutable callback-local creation token, never a persistent entity reference.</summary>
public readonly struct PendingEntity
{
    internal readonly StructuralCommandBuffer? Buffer;
    internal readonly ulong Invocation;
    internal readonly int Slot;
    internal PendingEntity(StructuralCommandBuffer buffer, ulong invocation, int slot)
    { Buffer = buffer; Invocation = invocation; Slot = slot; }
}

/// <summary>Playback may have partially mutated ECS. Always stop the Simulation.</summary>
public sealed class StructuralCommandPlaybackException : Exception
{
    public StructuralCommandPlaybackException(string message, Exception inner) : base(message, inner) { }
}

internal interface ICommandComponentPool
{
    EcsDefaultWorld CommandWorld { get; }
    int CommandCapacity { get; }
    bool IsDynamic { get; }
}

/// <summary>
/// Dense bounded command and payload storage shared by nonoverlapping callbacks.
/// Submission and full validation never mutate ECS; unexpected playback failures are fatal.
/// The owning ComponentCallbackScope must be active until End.
/// </summary>
public sealed class StructuralCommandBuffer : IEcsWorldEventListener, IDisposable
{
    private readonly EcsDefaultWorld _world;
    private readonly ComponentCallbackScope _scope;
    private readonly DynamicComponentStorage _dynamic;
    private readonly Command[] _commands;
    private readonly byte[] _payload;
    private readonly TargetState[] _targets;
    private readonly PoolState[] _pools;
    private readonly Membership[] _membership;
    private readonly int _pendingLimit;
    private int[] _targetMap;
    private int _count, _pending, _bytes, _targetCount;
    private int _anchorCount;
    private string? _owner;
    private ulong _invocation;
    private bool _active, _invalid, _completed, _playing, _disposed, _scratchUsed;

    internal StructuralCommandBuffer(EcsDefaultWorld world, ComponentCallbackScope scope,
        DynamicComponentStorage dynamic, StructuralCommandOptions options)
    {
        if (options.MaxCommands < 0 || options.MaxPendingEntities < 0 || options.MaxPayloadBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Command limits cannot be negative.");
        int scratch = 4;
        int requested = checked(Math.Max(1, options.MaxCommands) * 4);
        while (scratch < requested) scratch = checked(scratch * 2);
        _world = world; _scope = scope; _dynamic = dynamic;
        _commands = new Command[options.MaxCommands];
        _targets = new TargetState[options.MaxCommands];
        _payload = new byte[options.MaxPayloadBytes];
        _pools = new PoolState[scratch]; _membership = new Membership[scratch];
        _pendingLimit = Math.Min(options.MaxCommands, options.MaxPendingEntities);
        _targetMap = new int[world.Capacity]; Array.Fill(_targetMap, -1);
        world.AddListener(this);
    }

    public void Begin(string owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _scope.Require(owner);
        if (_active) { _invalid = true; throw new InvalidOperationException("A command callback is already active."); }
        if (_invocation == ulong.MaxValue) throw new InvalidOperationException("Command invocation identity exhausted.");
        _invocation++; _owner = owner; _active = true; _invalid = false; _completed = false;
    }

    public void RequireOwner(string owner)
    {
        try { Require(); if (_owner != owner) throw new InvalidOperationException("Commands belong to another system."); }
        catch { Invalidate(); throw; }
    }

    public PendingEntity Create()
    {
        try
        {
            Require(); RequireRoom();
            if (_pending == _pendingLimit) throw new InvalidOperationException("Pending entity limit exceeded.");
            var token = new PendingEntity(this, _invocation, _pending++);
            _commands[_count++] = new Command { Kind = Kind.Create, Target = -token.Slot - 1 };
            return token;
        }
        catch { Invalidate(); throw; }
    }

    public void Add(PreparedComponentHandle pool, int entity, ComponentSnapshot? snapshot = null) => Enqueue(pool, entity, default, false, true, snapshot);
    public void Add(PreparedComponentHandle pool, PendingEntity entity, ComponentSnapshot? snapshot = null) => Enqueue(pool, 0, entity, true, true, snapshot);
    public void Del(PreparedComponentHandle pool, int entity) => Enqueue(pool, entity, default, false, false, null);
    public void Del(PreparedComponentHandle pool, PendingEntity entity) => Enqueue(pool, 0, entity, true, false, null);

    private void Enqueue(PreparedComponentHandle handle, int entity, PendingEntity pending, bool isPending, bool add, ComponentSnapshot? snapshot)
    {
        try
        {
            Require(); RequireRoom();
            ArgumentNullException.ThrowIfNull(handle);
            handle.RequireMutable();
            if (handle.ClrType == typeof(DynamicComponentAnchor))
                throw new InvalidOperationException("The technical dynamic component anchor cannot be changed by commands.");
            if (!ReferenceEquals(handle.SimulationScope, _scope) || handle.Pool is not ICommandComponentPool capability ||
                !ReferenceEquals(capability.CommandWorld, _world))
                throw new InvalidOperationException("Command pool belongs to another Simulation.");
            int target; short generation = 0;
            if (isPending)
            {
                if (!ReferenceEquals(pending.Buffer, this) || pending.Invocation != _invocation || (uint)pending.Slot >= (uint)_pending)
                    throw new InvalidOperationException("Pending entity belongs to another callback or Simulation.");
                target = -pending.Slot - 1;
            }
            else
            {
                if ((uint)entity >= (uint)_world.Capacity || !_world.IsUsed(entity))
                    throw new InvalidOperationException("Command target is not alive.");
                target = entity; generation = _world.GetEntityLong(entity).Gen;
            }
            int offset = -1;
            if (snapshot is not null)
            {
                ValidateSnapshot(handle, snapshot);
                int required = 0;
                foreach (var field in handle.Fields.Span)
                    if (field.CanWrite) required = checked(required + StructuralCommandPayload.Size(field.Kind));
                if (required > _payload.Length - _bytes) throw new InvalidOperationException("Command payload limit exceeded.");
                offset = _bytes;
                var fields = handle.Fields.Span;
                for (int i = 0; i < fields.Length; i++)
                {
                    if (!fields[i].CanWrite) continue;
                    int size = StructuralCommandPayload.Size(fields[i].Kind);
                    StructuralCommandPayload.Capture(snapshot, i, fields[i].Kind, _payload.AsSpan(_bytes, size));
                    _bytes += size;
                }
            }
            _commands[_count++] = new Command { Kind = add ? Kind.Add : Kind.Del, Handle = handle, Target = target, Generation = generation, PayloadOffset = offset };
        }
        catch { Invalidate(); throw; }
    }

    private static void ValidateSnapshot(PreparedComponentHandle handle, ComponentSnapshot snapshot)
    {
        snapshot.ValidateAccess();
        if (!ReferenceEquals(snapshot.SimulationScope, handle.SimulationScope) || snapshot.ComponentId != handle.ComponentId || snapshot.ClrType != handle.ClrType)
            throw new InvalidOperationException("Snapshot belongs to another component or Simulation.");
        var source = snapshot.Fields.Span; var target = handle.Fields.Span;
        if (source.Length != target.Length) throw new InvalidOperationException("Incompatible snapshot layout.");
        for (int i = 0; i < source.Length; i++)
        {
            var a = source[i]; var b = target[i];
            if (a.Name != b.Name || a.Kind != b.Kind || a.CanWrite != b.CanWrite || !a.Path.Span.SequenceEqual(b.Path.Span) ||
                !ReferenceEquals(a.PointerRegistry, b.PointerRegistry) || a.PointerType.RegistryId != b.PointerType.RegistryId || a.PointerType.Slot != b.PointerType.Slot)
                throw new InvalidOperationException("Incompatible snapshot layout.");
        }
    }

    public void Complete()
    {
        try
        {
            Require();
            // Existing Lua systems without commands must not scan the reserved validation arena.
            if (_count == 0) { _completed = true; return; }
            Preflight();
        }
        catch { Invalidate(); throw; }
        _playing = true;
        try { Playback(); _completed = true; }
        catch (Exception error)
        {
            _invalid = true;
            if (error is OutOfMemoryException or OperationCanceledException or StructuralCommandPlaybackException) throw;
            throw new StructuralCommandPlaybackException("Structural command playback failed; stop the Simulation.", error);
        }
        finally { _playing = false; }
    }

    private void Preflight()
    {
        if (_pending > _world.Capacity - 1 - _world.Count - _world.DelEntBufferCount)
            throw new InvalidOperationException("Reserved entity capacity is insufficient for this batch.");
        _targetCount = _pending; _anchorCount = _dynamic.CommandAnchorCount;
        for (int i = 0; i < _pending; i++) _targets[i] = new TargetState { Pending = true };
        int materialized = 0;
        for (int i = 0; i < _count; i++)
        {
            ref var command = ref _commands[i];
            if (command.Kind == Kind.Create) continue;
            var handle = command.Handle!;
            handle.RequireMutable();
            int targetIndex = GetTarget(command.Target, command.Generation, materialized);
            command.StateIndex = targetIndex;
            ref var target = ref _targets[targetIndex];
            int poolIndex = GetPool(handle.Pool); command.PoolIndex = poolIndex;
            ref var pool = ref _pools[poolIndex];
            ref var membership = ref GetMembership(poolIndex, targetIndex, target.Pending ? false : handle.Pool.Has(target.Entity));
            bool add = command.Kind == Kind.Add;
            if (membership.Present == add)
                throw new InvalidOperationException(add ? "Command adds an existing component." : "Command removes a missing component.");
            if (!add && target.Logical <= 1)
                throw new InvalidOperationException("Deferred removal of the final component is not allowed.");
            if (add && target.Pending && target.Logical == 0) materialized++;
            int delta = add ? 1 : -1;
            pool.Count += delta;
            if ((uint)pool.Count > (uint)((ICommandComponentPool)pool.Pool!).CommandCapacity)
                throw new InvalidOperationException("Reserved component pool capacity is insufficient for this batch.");
            if (((ICommandComponentPool)pool.Pool!).IsDynamic)
            {
                if (add && target.Dynamic == 0) _anchorCount++;
                if (!add && target.Dynamic == 1) _anchorCount--;
                target.Dynamic += delta;
                if ((uint)_anchorCount > (uint)_dynamic.CommandAnchorCapacity)
                    throw new InvalidOperationException("Reserved dynamic anchor capacity is insufficient for this batch.");
            }
            target.Logical += delta; membership.Present = add;
            ValidatePayload(command);
        }
        for (int i = 0; i < _pending; i++)
            if (_targets[i].Logical == 0) throw new InvalidOperationException("Every pending entity requires a component.");
        // Restore initial state for invariant checks during application.
        for (int i = 0; i < _targetCount; i++) { _targets[i].Logical = _targets[i].InitialLogical; _targets[i].Dynamic = _targets[i].InitialDynamic; }
        for (int i = 0; i < _pools.Length; i++) if (_pools[i].Pool is { } pool) _pools[i].Count = pool.Count;
        _anchorCount = _dynamic.CommandAnchorCount;
    }

    private int GetTarget(int entity, short generation, int materialized)
    {
        if (entity < 0) return -entity - 1;
        if (!_world.IsAliveSafe(entity, generation)) throw new InvalidOperationException("Command target identity changed before playback.");
        int index = _targetMap[entity];
        if (index >= 0) return index;
        int dynamic = _dynamic.CommandDynamicCount(entity);
        int logical = _world.GetComponentsCount(entity) - (dynamic > 0 ? 1 : 0) + dynamic;
        if (logical == 0 && materialized > 0)
            throw new InvalidOperationException("An empty entity target can be collected before its queued initialization.");
        index = _targetCount++;
        _targets[index] = new TargetState { Entity = entity, Generation = generation, Logical = logical, InitialLogical = logical, Dynamic = dynamic, InitialDynamic = dynamic };
        _targetMap[entity] = index;
        return index;
    }

    private int GetPool(IComponentPoolAccess pool)
    {
        _scratchUsed = true;
        int mask = _pools.Length - 1;
        int slot = RuntimeHelpers.GetHashCode(pool) & mask;
        for (int probe = 0; probe < _pools.Length; probe++, slot = (slot + 1) & mask)
        {
            if (ReferenceEquals(_pools[slot].Pool, pool)) return slot;
            if (_pools[slot].Pool is null) { _pools[slot] = new PoolState { Pool = pool, Count = pool.Count }; return slot; }
        }
        throw new InvalidOperationException("Command validation pool scratch is exhausted.");
    }

    private ref Membership GetMembership(int pool, int target, bool initial)
    {
        int mask = _membership.Length - 1;
        int slot = (int)((unchecked((uint)pool * 0x9E3779B9u) ^ unchecked((uint)target * 0x85EBCA6Bu)) & (uint)mask);
        for (int probe = 0; probe < _membership.Length; probe++, slot = (slot + 1) & mask)
        {
            ref var item = ref _membership[slot];
            if (item.Used && item.Pool == pool && item.Target == target) return ref item;
            if (!item.Used) { item = new Membership { Used = true, Pool = pool, Target = target, Present = initial }; return ref item; }
        }
        throw new InvalidOperationException("Command validation membership scratch is exhausted.");
    }

    private void ValidatePayload(in Command command)
    {
        if (command.PayloadOffset < 0) return;
        int offset = command.PayloadOffset;
        foreach (var field in command.Handle!.Fields.Span)
        {
            if (!field.CanWrite) continue;
            int size = StructuralCommandPayload.Size(field.Kind);
            StructuralCommandPayload.ValidatePointer(field, _payload.AsSpan(offset, size)); offset += size;
        }
    }

    private void Playback()
    {
        for (int i = 0; i < _count; i++)
        {
            ref readonly var command = ref _commands[i];
            if (command.Kind == Kind.Create) continue;
            ref var target = ref _targets[command.StateIndex];
            if (target.Pending && target.Entity == 0) Materialize(ref target);
            RequireActualTarget(target);
            var pool = command.Handle!.Pool;
            ref var poolState = ref _pools[command.PoolIndex];
            if (pool.Count != poolState.Count) throw InvariantFailure();
            bool add = command.Kind == Kind.Add;
            int delta = add ? 1 : -1;
            bool dynamic = ((ICommandComponentPool)pool).IsDynamic;
            int anchorDelta = dynamic ? (add && target.Dynamic == 0 ? 1 : !add && target.Dynamic == 1 ? -1 : 0) : 0;
            long version = _world.Version; int count = _world.Count, deleted = _world.DelEntBufferCount, capacity = _world.Capacity;
            if (add) pool.Add(target.Entity); else pool.Del(target.Entity);
            target.Logical += delta; if (dynamic) target.Dynamic += delta;
            poolState.Count += delta; _anchorCount += anchorDelta;
            if (_world.Version != unchecked(version + (dynamic ? Math.Abs(anchorDelta) : 1)) || _world.Count != count || _world.DelEntBufferCount != deleted ||
                _world.Capacity != capacity || pool.Count != poolState.Count || _dynamic.CommandAnchorCount != _anchorCount)
                throw InvariantFailure();
            RequireActualTarget(target);
            if (command.PayloadOffset >= 0)
            {
                version = _world.Version;
                int offset = command.PayloadOffset;
                foreach (var field in command.Handle.Fields.Span)
                {
                    if (!field.CanWrite) continue;
                    int size = StructuralCommandPayload.Size(field.Kind);
                    StructuralCommandPayload.Apply(field, target.Entity, _payload.AsSpan(offset, size)); offset += size;
                    if (_world.Version != version || _world.Count != count || _world.DelEntBufferCount != deleted || _world.Capacity != capacity ||
                        pool.Count != poolState.Count || _dynamic.CommandAnchorCount != _anchorCount) throw InvariantFailure();
                    RequireActualTarget(target);
                }
            }
        }
    }

    private void Materialize(ref TargetState target)
    {
        long version = _world.Version; int count = _world.Count, deleted = _world.DelEntBufferCount, capacity = _world.Capacity;
        target.Entity = _world.NewEntity();
        int cleaned = _world.DelEntBufferCount - deleted;
        // NewEntity may normally collect pre-existing empty entities when its backend buffer fills.
        if (cleaned < 0 || _world.Version != unchecked(version + 1 + cleaned) || _world.Count != count + 1 - cleaned ||
            _world.Capacity != capacity || !_world.IsUsed(target.Entity) || _world.GetComponentsCount(target.Entity) != 0)
            throw InvariantFailure();
        target.Generation = _world.GetEntityLong(target.Entity).Gen;
    }

    private void RequireActualTarget(in TargetState target)
    {
        if (!_world.IsAliveSafe(target.Entity, target.Generation)) throw InvariantFailure();
        int dynamic = _dynamic.CommandDynamicCount(target.Entity);
        int logical = _world.GetComponentsCount(target.Entity) - (dynamic > 0 ? 1 : 0) + dynamic;
        if (dynamic != target.Dynamic || logical != target.Logical) throw InvariantFailure();
    }

    private static InvalidOperationException InvariantFailure() => new("Unexpected ECS mutation during structural command playback.");
    private void RequireRoom() { if (_count == _commands.Length) throw new InvalidOperationException("Command count limit exceeded."); }
    private void Require()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_active || _owner is null) throw new InvalidOperationException("Commands require an active owning callback.");
        _scope.Require(_owner);
        if (_invalid) throw new InvalidOperationException("The callback command batch is invalid.");
        if (_playing || _completed) throw new InvalidOperationException("Command submission is closed.");
    }

    public void Invalidate() { if (_active) _invalid = true; }
    public void End()
    {
        for (int i = _pending; i < _targetCount; i++) _targetMap[_targets[i].Entity] = -1;
        Array.Clear(_commands, 0, _count); Array.Clear(_targets, 0, _targetCount);
        if (_scratchUsed) { Array.Clear(_pools); Array.Clear(_membership); _scratchUsed = false; }
        _payload.AsSpan(0, _bytes).Clear();
        _count = _pending = _bytes = _targetCount = 0;
        _active = _invalid = _completed = _playing = false; _owner = null;
    }

    public void OnWorldResize(int newSize)
    {
        int old = _targetMap.Length; Array.Resize(ref _targetMap, newSize); Array.Fill(_targetMap, -1, old, newSize - old);
    }
    public void OnReleaseDelEntityBuffer(ReadOnlySpan<int> buffer) { }
    public void OnWorldDestroy() { End(); _disposed = true; }
    public void Dispose()
    {
        if (_disposed) return;
        _world.RemoveListener(this); End(); _disposed = true;
    }

    private enum Kind : byte { Create, Add, Del }
    private struct Command
    {
        internal Kind Kind;
        internal PreparedComponentHandle? Handle;
        internal int Target, PayloadOffset, StateIndex, PoolIndex;
        internal short Generation;
    }
    private struct TargetState
    {
        internal int Entity, Logical, InitialLogical, Dynamic, InitialDynamic;
        internal short Generation;
        internal bool Pending;
    }
    private struct PoolState { internal IComponentPoolAccess? Pool; internal int Count; }
    private struct Membership { internal bool Used, Present; internal int Pool, Target; }
}
