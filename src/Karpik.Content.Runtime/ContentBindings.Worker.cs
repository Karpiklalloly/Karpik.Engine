using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Karpik.Content.Core;

namespace Karpik.Content.Runtime;

public sealed partial class ContentBindings
{
    private async Task DispatchAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(2));
        try
        {
            do
            {
                for (int i = 0; i < _work.Length; i++)
                {
                    var work = Volatile.Read(ref _work[i]);
                    if (work == null) continue;
                    if (Volatile.Read(ref work.Control) == BindingWork.Running)
                    {
                        if (work.ActiveTask is not { IsCompleted: true }) continue;
                        await work.ActiveTask.ConfigureAwait(false);
                        work.ActiveTask = null;
                        if (Volatile.Read(ref work.Accepting) == 0)
                        {
                            Interlocked.Exchange(ref work.Result, null)?.ReleasePin();
                            work.Adapter = null;
                            Volatile.Write(ref work.Control, BindingWork.Stopped);
                            continue;
                        }
                        Volatile.Write(ref work.Control, BindingWork.Idle);
                    }
                    if (Volatile.Read(ref work.Accepting) == 0)
                    {
                        // Release may race between the completed-task accepting
                        // check above and its Running -> Idle transition.
                        if (Interlocked.CompareExchange(ref work.Control, BindingWork.Stopped, BindingWork.Idle) == BindingWork.Idle)
                        {
                            Interlocked.Exchange(ref work.Result, null)?.ReleasePin();
                            work.Adapter = null;
                        }
                        continue;
                    }
                    if (!work.TryReadRequest(out ulong epoch, out uint version, out bool load) ||
                        !load || work.ScheduledEpoch == epoch ||
                        Interlocked.CompareExchange(ref work.Control, BindingWork.Running, BindingWork.Idle) != BindingWork.Idle)
                        continue;
                    if (Volatile.Read(ref work.Accepting) == 0)
                    {
                        work.Adapter = null;
                        Volatile.Write(ref work.Control, BindingWork.Stopped);
                        continue;
                    }
                    work.ScheduledEpoch = epoch;
                    work.ActiveTask = Task.Run(() => LoadOneAsync(work, epoch, version));
                }
            } while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task LoadOneAsync(BindingWork work, ulong epoch, uint version)
    {
        BindingResult result;
        try
        {
            _stop.Token.ThrowIfCancellationRequested();
            object value = await work.Adapter!.LoadAsync(_registry, work.Id, version, _stop.Token).ConfigureAwait(false);
            _stop.Token.ThrowIfCancellationRequested();
            ContentResourceLifetime? pin = (value as IContentTrackedResource)?.Lifetime;
            if (pin != null && !pin.TryRetain()) throw new InvalidOperationException("Content payload retired before binding adoption.");
            result = new BindingResult(epoch, version, value, null, pin);
        }
        catch (Exception error)
        {
            result = new BindingResult(epoch, version, null, error, null);
        }
        if (Volatile.Read(ref work.Accepting) == 0 || Volatile.Read(ref _stopping) != 0)
        {
            result.ReleasePin();
            return;
        }
        Interlocked.Exchange(ref work.Result, result)?.ReleasePin();
    }

    private sealed class BindingWork(AssetId id, IBindingAdapter adapter)
    {
        internal const int Idle = 0, Running = 1, Stopped = 2;
        internal readonly AssetId Id = id;
        internal IBindingAdapter? Adapter = adapter;
        internal readonly Exception IncompatibleError = new InvalidOperationException("The Content ID is bound to an incompatible CLR payload type.");
        internal int Accepting = 1;
        internal int Control;
        internal Task? ActiveTask;
        internal BindingResult? Result;
        internal ulong ScheduledEpoch;
        private int _sequence;
        private ulong _epoch;
        private uint _version;
        private bool _load;

        internal void PublishRequest(ulong epoch, uint version, bool load)
        {
            // Full fence publishes the odd writer marker before any descriptor
            // fields, including on weakly ordered ARM64 hosts.
            int sequence = Interlocked.Increment(ref _sequence);
            _epoch = epoch; _version = version; _load = load;
            Volatile.Write(ref _sequence, unchecked(sequence + 1));
        }
        internal bool TryReadRequest(out ulong epoch, out uint version, out bool load)
        {
            int before = Volatile.Read(ref _sequence);
            epoch = _epoch; version = _version; load = _load;
            Thread.MemoryBarrier();
            return (before & 1) == 0 && before == Volatile.Read(ref _sequence);
        }
    }

    private sealed class BindingResult(ulong epoch, uint version, object? value, Exception? error, ContentResourceLifetime? pin)
    {
        internal readonly ulong Epoch = epoch;
        internal readonly uint Version = version;
        internal readonly object? Value = value;
        internal readonly Exception? Error = error;
        private ContentResourceLifetime? _pin = pin;
        internal ContentResourceLifetime? TakePin() => Interlocked.Exchange(ref _pin, null);
        internal void ReleasePin() => TakePin()?.Release();
    }

    private interface IBindingAdapter
    {
        bool CanAdoptAtBoundary { get; }
        bool TryResolve(IContentUpdateRegistry registry, AssetId id, out uint version);
        bool TryGet(IContentRegistry registry, AssetId id, uint version, out object? value);
        Task<object> LoadAsync(IContentRegistry registry, AssetId id, uint version, CancellationToken ct);
    }
    private sealed class DirectAdapter<T> : IBindingAdapter
    {
        public bool CanAdoptAtBoundary { get; } = !typeof(T).IsValueType;
        public bool TryResolve(IContentUpdateRegistry registry, AssetId id, out uint version)
        {
            bool found = registry.TryResolve<T>(id, out var reference); version = reference.Version; return found;
        }
        public bool TryGet(IContentRegistry registry, AssetId id, uint version, out object? value)
        {
            if (registry.TryGet(new AssetRef<T>(id, version), out var lease)) { value = lease.Payload; return value != null; }
            value = null; return false;
        }
        public async Task<object> LoadAsync(IContentRegistry registry, AssetId id, uint version, CancellationToken ct)
        {
            // A registry load is shared across binding owners. Cancel only this
            // owner's wait; registry shutdown drains the underlying stable loader.
            await registry.LoadAsync(new AssetRef<T>(id, version), CancellationToken.None).WaitAsync(ct).ConfigureAwait(false);
            if (!TryGet(registry, id, version, out var value)) throw new InvalidOperationException("Content binding load no longer refers to the current generation.");
            return value!;
        }
    }
    private sealed class JsonAdapter<T>(JsonTypeInfo<T> typeInfo) : IBindingAdapter
    {
        public bool CanAdoptAtBoundary => false;
        public bool TryResolve(IContentUpdateRegistry registry, AssetId id, out uint version)
        {
            bool found = registry.TryResolve<RawJsonPayload>(id, out var reference); version = reference.Version; return found;
        }
        public bool TryGet(IContentRegistry registry, AssetId id, uint version, out object? value) { value = null; return false; }
        public async Task<object> LoadAsync(IContentRegistry registry, AssetId id, uint version, CancellationToken ct)
        {
            var reference = new AssetRef<RawJsonPayload>(id, version);
            await registry.LoadAsync(reference, CancellationToken.None).WaitAsync(ct).ConfigureAwait(false);
            if (!registry.TryGet(reference, out var lease)) throw new InvalidOperationException("JSON binding no longer refers to the current generation.");
            ct.ThrowIfCancellationRequested();
            return JsonSerializer.Deserialize(lease.Payload.Json, typeInfo)
                ?? throw new InvalidDataException("JSON binding projection returned null.");
        }
    }
}
