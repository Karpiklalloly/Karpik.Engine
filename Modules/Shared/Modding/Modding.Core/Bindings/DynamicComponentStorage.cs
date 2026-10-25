using DCFApixels.DragonECS;

namespace Karpik.Engine.Shared.Modding.Bindings;

public sealed class DynamicComponentStorage : IEcsWorldEventListener, IDisposable
{
    private readonly EcsDefaultWorld _world;
    private readonly EcsPool<DynamicComponentAnchor> _anchors;
    private readonly Dictionary<string, DynamicF32ComponentPool> _byId = new(StringComparer.Ordinal);
    private readonly DynamicF32ComponentPool[] _pools;
    private int[] _counts;
    private bool _disposed;

    public DynamicComponentStorage(EcsDefaultWorld world, ComponentSchemaRegistry schemas)
    {
        _world = world;
        _anchors = world.GetPool<DynamicComponentAnchor>();
        _counts = new int[world.Capacity];
        var descriptions = schemas.GetSchemas();
        _pools = new DynamicF32ComponentPool[descriptions.Length];
        int capacity = world.Configs.TryGet<EcsWorldConfig>(out var config) ? config.PoolComponentsCapacity : EcsWorldConfig.Default.PoolComponentsCapacity;
        for (int i = 0; i < descriptions.Length; i++)
        {
            _pools[i] = new DynamicF32ComponentPool(this, descriptions[i], world.Capacity, capacity);
            _byId.Add(descriptions[i].Id, _pools[i]);
        }
        world.AddListener(this);
    }

    public IComponentPoolAccess Resolve(string schemaId)
    {
        ThrowIfDisposed();
        return _byId.TryGetValue(schemaId, out var pool) ? pool : throw new InvalidDataException($"Unknown dynamic component '{schemaId}'.");
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    internal void ValidateEntity(int entityId)
    {
        ThrowIfDisposed();
        if ((uint)entityId >= (uint)_counts.Length || !_world.IsUsed(entityId))
            throw new InvalidOperationException("Entity is not alive in this world.");
    }

    internal void Added(int entityId)
    {
        if (_counts[entityId] == 0) _anchors.Add(entityId);
        _counts[entityId]++;
    }

    internal void Removed(int entityId)
    {
        if (--_counts[entityId] == 0) _anchors.Del(entityId);
    }

    public void OnWorldResize(int newSize)
    {
        Array.Resize(ref _counts, newSize);
        foreach (var pool in _pools) pool.ResizeMapping(newSize);
    }

    public void OnReleaseDelEntityBuffer(ReadOnlySpan<int> buffer)
    {
        foreach (int entity in buffer)
        {
            foreach (var pool in _pools) pool.RemoveReleasedEntity(entity);
            _counts[entity] = 0;
        }
    }

    public void OnWorldDestroy()
    {
        _disposed = true;
        foreach (var pool in _pools) pool.Clear();
        _counts = [];
        // Do not remove a listener while DragonECS iterates its listener collection.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _world.RemoveListener(this);
        for (int entity = 0; entity < _counts.Length; entity++)
            if (_counts[entity] > 0 && _world.IsUsed(entity) && _anchors.Has(entity)) _anchors.Del(entity);
        OnWorldDestroy();
    }
}
