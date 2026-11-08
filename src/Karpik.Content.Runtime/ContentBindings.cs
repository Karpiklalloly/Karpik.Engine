using System.Composition;
using System.Text.Json.Serialization.Metadata;
using Karpik.Content.Core;
using Karpik.Engine.Core;

namespace Karpik.Content.Runtime;

[Export(typeof(IContentBindings))]
[Export(typeof(ContentBindings))]
[Export(typeof(IContentBindingRuntime))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed partial class ContentBindings : IContentBindings, IContentBindingRuntime
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly IContentRegistry _registry;
    private readonly IContentUpdateRegistry _updates;
    private readonly IContentChangeSource _changes;
    private readonly BindingSlot[] _slots;
    private readonly int[] _free;
    private readonly BindingWork?[] _work;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _disposeGate = new();
    private Task? _worker;
    private Task? _disposeTask;
    private Task _stopCallbacks = Task.CompletedTask;
    private uint _observedRevision;
    private int _freeCount;
    private int _stopping;
    private int _disposed;

    public ContentBindings(IContentRegistry registry, IContentUpdateRegistry updates, IContentChangeSource changes,
        ContentBindingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(changes);
        int capacity = options?.Capacity ?? ContentBindingOptions.DefaultCapacity;
        if (capacity is < 1 or > ContentBindingOptions.MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(options), "Binding capacity must be between 1 and 4096.");
        _registry = registry;
        _updates = updates;
        _changes = changes;
        _slots = new BindingSlot[capacity];
        _free = new int[capacity];
        _work = new BindingWork[capacity];
        _freeCount = capacity;
        for (int i = 0; i < capacity; i++) _free[i] = capacity - i - 1;
    }

    public ContentBinding<T> Bind<T>(AssetId id) => Register<T>(id, default!, false, null);
    public ContentBinding<T> Bind<T>(AssetId id, T fallback) => Register(id, fallback, fallback is not null, null);
    public ContentBinding<T> BindJson<T>(AssetId id, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return Register<T>(id, default!, false, typeInfo);
    }
    public ContentBinding<T> BindJson<T>(AssetId id, JsonTypeInfo<T> typeInfo, T fallback)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return Register(id, fallback, fallback is not null, typeInfo);
    }

    private ContentBinding<T> Register<T>(AssetId id, T fallback, bool hasFallback, JsonTypeInfo<T>? typeInfo)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
        if (fallback is IContentTrackedResource { Lifetime: not null })
            throw new ArgumentException("A registry-generation resource cannot be a constant fallback.", nameof(fallback));
        ReclaimDrainedSlots();
        if (_freeCount == 0) throw new InvalidOperationException("Content binding capacity exhausted. Configure capacity before presentation starts.");
        int index = _free[--_freeCount];
        ref BindingSlot slot = ref _slots[index];
        uint generation = slot.Generation == 0 ? 1 : slot.Generation;
        slot = new BindingSlot
        {
            Active = true,
            Generation = generation,
            Id = id,
            ValueType = typeof(T),
            Current = hasFallback ? fallback : null,
            Fallback = hasFallback ? fallback : null,
            JsonTypeInfo = typeInfo,
            HasFallback = hasFallback,
            Status = new ContentBindingStatus(ContentBindingState.Pending, hasFallback, hasFallback, 0, 0),
            RequestEpoch = 1,
            NeedsResolve = true
        };
        IBindingAdapter adapter = typeInfo == null ? new DirectAdapter<T>() : new JsonAdapter<T>(typeInfo);
        Volatile.Write(ref _work[index], new BindingWork(id, adapter));
        // One persistent dispatcher, started lazily. Task creation never occurs at a frame boundary.
        _worker ??= Task.Run(DispatchAsync);
        return new ContentBinding<T>(_owner, index, generation);
    }

    public bool TryGet<T>(ContentBinding<T> binding, out T value)
    {
        if (IsValid(binding) && _slots[binding.Slot].Status.HasValue)
        {
            value = (T)_slots[binding.Slot].Current!;
            return true;
        }
        value = default!;
        return false;
    }

    public bool TryGetStatus<T>(ContentBinding<T> binding, out ContentBindingStatus status)
    {
        if (IsValid(binding)) { status = _slots[binding.Slot].Status; return true; }
        status = default;
        return false;
    }

    public bool Release<T>(ContentBinding<T> binding)
    {
        if (!IsValid(binding)) return false;
        ref BindingSlot slot = ref _slots[binding.Slot];
        slot.Active = false;
        slot.Draining = true;
        ReleaseSelection(ref slot);
        slot.Current = null;
        slot.Fallback = null;
        slot.JsonTypeInfo = null;
        slot.ValueType = null;
        var work = Volatile.Read(ref _work[binding.Slot])!;
        Volatile.Write(ref work.Accepting, 0);
        Interlocked.CompareExchange(ref work.Control, BindingWork.Stopped, BindingWork.Idle);
        if (Volatile.Read(ref work.Control) == BindingWork.Stopped) FreeSlot(binding.Slot, work);
        return true;
    }

    public bool Retry<T>(ContentBinding<T> binding)
    {
        if (Volatile.Read(ref _stopping) != 0 || !IsValid(binding)) return false;
        ref BindingSlot slot = ref _slots[binding.Slot];
        slot.RequestEpoch = checked(slot.RequestEpoch + 1);
        slot.Error = null;
        slot.Status = slot.Status with { State = ContentBindingState.Pending };
        slot.NeedsResolve = true;
        return true;
    }

    public Exception? GetError<T>(ContentBinding<T> binding) => IsValid(binding) ? _slots[binding.Slot].Error : null;

    private bool IsValid<T>(ContentBinding<T> binding)
    {
        if (Volatile.Read(ref _disposed) != 0 || binding.Owner != _owner || (uint)binding.Slot >= (uint)_slots.Length) return false;
        ref BindingSlot slot = ref _slots[binding.Slot];
        return slot.Active && slot.Generation == binding.Generation && slot.ValueType == typeof(T);
    }

    public void AdvanceBoundary()
    {
        if (Volatile.Read(ref _stopping) != 0) return;
        var source = _registry as ContentRegistry;
        uint revision = source?.CurrentRevision ?? 0;
        bool changed = source == null || revision != _observedRevision;
        for (int i = 0; i < _slots.Length; i++)
        {
            ref BindingSlot slot = ref _slots[i];
            if (!slot.Active) continue;
            var work = Volatile.Read(ref _work[i])!;
            if (changed || slot.NeedsResolve)
            {
                slot.NeedsResolve = false;
                Refresh(ref slot, work, source);
            }
            if (Volatile.Read(ref work.Result) == null) continue;
            BindingResult? result = Interlocked.Exchange(ref work.Result, null);
            if (result == null) continue;
            if (result.Epoch != slot.RequestEpoch || result.Version != slot.Status.RequestedVersion)
            {
                result.ReleasePin();
                continue;
            }
            if (result.Error != null)
            {
                slot.Error = result.Error;
                slot.Status = slot.Status with { State = ContentBindingState.Failed };
            }
            else
            {
                SetSelection(ref slot, result.Value!, result.TakePin(), result.Version);
            }
        }
        _observedRevision = revision;
    }

    private void Refresh(ref BindingSlot slot, BindingWork work, ContentRegistry? source)
    {
        var adapter = work.Adapter!;
        if (!adapter.TryResolve(_updates, work.Id, out uint version))
        {
            bool incompatible = source != null && source.TryGetVersion(work.Id, out version);
            if (slot.Status.RequestedVersion != version || slot.Status.State is not (ContentBindingState.Missing or ContentBindingState.Failed))
                slot.RequestEpoch = checked(slot.RequestEpoch + 1);
            if (!incompatible || slot.Current is IContentTrackedResource) UseFallback(ref slot);
            slot.Error = incompatible ? work.IncompatibleError : null;
            slot.Status = new ContentBindingStatus(incompatible ? ContentBindingState.Failed : ContentBindingState.Missing,
                slot.Status.HasValue, slot.Status.IsFallback, version, slot.Status.PresentedVersion);
            work.PublishRequest(slot.RequestEpoch, version, false);
            return;
        }
        bool requested = slot.Status.RequestedVersion != version || slot.Status.State == ContentBindingState.Pending;
        if (!requested) return;
        if (slot.Status.RequestedVersion != version) slot.RequestEpoch = checked(slot.RequestEpoch + 1);
        slot.Status = slot.Status with { State = ContentBindingState.Pending, RequestedVersion = version };
        slot.Error = null;
        if (adapter.CanAdoptAtBoundary && adapter.TryGet(_registry, work.Id, version, out object? ready))
        {
            ContentResourceLifetime? pin = (ready as IContentTrackedResource)?.Lifetime;
            if (pin == null || pin.TryRetain())
            {
                SetSelection(ref slot, ready!, pin, version);
                work.PublishRequest(slot.RequestEpoch, version, false);
                return;
            }
        }
        // A current registry-generation resource may never escape into a fresh
        // frame after its slot was replaced and retirement was requested.
        if (slot.Current is IContentTrackedResource && slot.Status.PresentedVersion != version) UseFallback(ref slot);
        work.PublishRequest(slot.RequestEpoch, version, true);
    }

    private static void SetSelection(ref BindingSlot slot, object value, ContentResourceLifetime? pin, uint version)
    {
        ReleaseSelection(ref slot);
        slot.Current = value;
        slot.Pin = pin;
        slot.Error = null;
        slot.Status = new ContentBindingStatus(ContentBindingState.Ready, true, false, version, version);
    }
    private static void ReleaseSelection(ref BindingSlot slot)
    {
        slot.Pin?.Release();
        slot.Pin = null;
    }
    private static void UseFallback(ref BindingSlot slot)
    {
        ReleaseSelection(ref slot);
        slot.Current = slot.Fallback;
        slot.Status = slot.Status with { HasValue = slot.HasFallback, IsFallback = slot.HasFallback, PresentedVersion = 0 };
    }

    private void ReclaimDrainedSlots()
    {
        for (int i = 0; i < _slots.Length; i++)
            if (_slots[i].Draining && Volatile.Read(ref _work[i]) is { } work && Volatile.Read(ref work.Control) == BindingWork.Stopped)
                FreeSlot(i, work);
    }
    private void FreeSlot(int index, BindingWork work)
    {
        Interlocked.Exchange(ref work.Result, null)?.ReleasePin();
        work.Adapter = null;
        Volatile.Write(ref _work[index], null);
        uint generation = _slots[index].Generation;
        _slots[index] = default;
        if (generation != uint.MaxValue)
        {
            _slots[index].Generation = generation + 1;
            _free[_freeCount++] = index;
        }
    }

    public void RequestStop()
    {
        // This is a cold lifecycle API. Publish the cancellation-drain task
        // before another teardown caller can enter DisposeCoreAsync.
        lock (_disposeGate)
        {
            if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
            _stopCallbacks = _stop.CancelAsync();
        }
    }
    public ValueTask DisposeAsync()
    {
        RequestStop();
        lock (_disposeGate) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        for (int i = 0; i < _slots.Length; i++) ReleaseSelection(ref _slots[i]);
        if (_worker != null) await _worker.ConfigureAwait(false);
        await _stopCallbacks.ConfigureAwait(false);
        for (int i = 0; i < _work.Length; i++)
        {
            var work = _work[i];
            if (work == null) continue;
            if (work.ActiveTask != null) await work.ActiveTask.ConfigureAwait(false);
            Interlocked.Exchange(ref work.Result, null)?.ReleasePin();
            work.Adapter = null;
        }
        Array.Clear(_slots);
        Array.Clear(_work);
        _stop.Dispose();
    }

    private struct BindingSlot
    {
        public bool Active;
        public uint Generation;
        public AssetId Id;
        public Type? ValueType;
        public object? Current;
        public object? Fallback;
        public JsonTypeInfo? JsonTypeInfo;
        public bool HasFallback;
        public ContentBindingStatus Status;
        public Exception? Error;
        public ulong RequestEpoch;
        public bool NeedsResolve;
        public bool Draining;
        public ContentResourceLifetime? Pin;
    }
}
