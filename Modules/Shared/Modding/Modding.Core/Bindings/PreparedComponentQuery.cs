using DCFApixels.DragonECS;

namespace Karpik.Engine.Shared.Modding.Bindings;

public sealed class PreparedComponentQuery : IEcsWorldEventListener, IDisposable
{
    private readonly EcsDefaultWorld _world;
    private readonly ComponentCallbackScope _scope;
    private readonly string _owner;
    private readonly PreparedComponentHandle[] _inc, _exc;
    private int[] _snapshot;
    private int _length, _cursor;
    private bool _disposed;
    public IReadOnlyDictionary<string, PreparedComponentHandle> Components { get; }

    internal PreparedComponentQuery(EcsDefaultWorld world, ComponentCallbackScope scope, string owner,
        Dictionary<string, PreparedComponentHandle> components, PreparedComponentHandle[] inc, PreparedComponentHandle[] exc)
    {
        _world = world; _scope = scope; _owner = owner; _inc = inc; _exc = exc;
        Components = components; _snapshot = new int[world.Capacity]; world.AddListener(this);
    }

    public void Capture()
    {
        Require();
        IComponentPoolAccess? source = null;
        foreach (var handle in _inc)
            if (source is null || handle.Pool.Count < source.Count) source = handle.Pool;
        int count;
        if (source is null)
        {
            var entities = _world.ToSpan(); count = entities.Count;
            for (int i = 0; i < count; i++) _snapshot[i] = entities[i];
        }
        else count = source.CopyEntityIds(_snapshot);
        _length = 0; _cursor = 0;
        for (int i = 0; i < count; i++)
        {
            int entity = _snapshot[i];
            if (!_world.IsUsed(entity)) continue;
            bool matches = true;
            foreach (var handle in _inc) if (!handle.Pool.Has(entity)) { matches = false; break; }
            if (!matches) continue;
            foreach (var handle in _exc) if (handle.Pool.Has(entity)) { matches = false; break; }
            if (matches) _snapshot[_length++] = entity;
        }
    }

    public bool MoveNext(out int entityId)
    {
        Require();
        if (_cursor < _length) { entityId = _snapshot[_cursor++]; return true; }
        entityId = 0; return false;
    }

    public void Reset() { _cursor = 0; _length = 0; }
    public void ValidateAccess() => Require();
    private void Require() { ObjectDisposedException.ThrowIf(_disposed, this); _scope.Require(_owner); }
    public void OnWorldResize(int newSize) => Array.Resize(ref _snapshot, newSize);
    public void OnReleaseDelEntityBuffer(ReadOnlySpan<int> buffer) { }
    public void OnWorldDestroy() { _disposed = true; _snapshot = []; Reset(); }
    public void Dispose() { if (_disposed) return; _world.RemoveListener(this); OnWorldDestroy(); }
}
