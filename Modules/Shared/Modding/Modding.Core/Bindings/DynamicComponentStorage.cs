using DCFApixels.DragonECS;

namespace Karpik.Engine.Shared.Modding.Bindings;

public sealed class DynamicComponentStorage : IEcsWorldEventListener, IDisposable
{
    private readonly EcsDefaultWorld _world;
    private readonly EcsPool<DynamicComponentAnchor> _anchors;
    private readonly int _initialPoolCapacity;
    private Catalog _active;
    private int[] _counts;
    private bool _disposed;
    private readonly ComponentPointerRegistry? _pointers;
    internal ComponentPointerRegistry Pointers => _pointers ?? throw new InvalidDataException("Pointer fields require an explicitly supplied host pointer policy.");

    public DynamicComponentStorage(EcsDefaultWorld world, ComponentSchemaRegistry schemas, ComponentPointerRegistry? pointers = null)
    {
        _world = world;
        _pointers = pointers;
        _anchors = world.GetPool<DynamicComponentAnchor>();
        _counts = new int[world.Capacity];
        _initialPoolCapacity = world.Configs.TryGet<EcsWorldConfig>(out var config) ? config.PoolComponentsCapacity : EcsWorldConfig.Default.PoolComponentsCapacity;
        var byId = new Dictionary<string, DynamicComponentPool>(StringComparer.Ordinal);
        foreach (var schema in schemas.GetSchemas()) byId.Add(schema.Id, CreatePool(schema));
        _active = new Catalog(byId);
        world.AddListener(this);
    }

    public IComponentPoolAccess Resolve(string schemaId)
    {
        ThrowIfDisposed();
        return _active.Resolve(schemaId);
    }

    private DynamicComponentPool CreatePool(ComponentSchema schema) => new(this, schema, _world.Capacity, _initialPoolCapacity);

    internal CatalogChange PrepareCatalog(ComponentSchema[] schemas, bool allowPoolChanges,
        IReadOnlyDictionary<string, ComponentMigration>? declarations)
    {
        ThrowIfDisposed();
        var byId = new Dictionary<string, DynamicComponentPool>(schemas.Length, StringComparer.Ordinal);
        var added = new List<DynamicComponentPool>();
        var replacements = new List<PoolMigration>();
        foreach (var schema in schemas)
        {
            if (_active.ById.TryGetValue(schema.Id, out var pool))
            {
                // An identical accepted schema may still carry the historical declaration.
                if (!SchemaTree.Same(schema, pool.Schema))
                {
                    if (declarations is null) throw new InvalidDataException($"Simulation schema '{schema.Id}' changed.");
                    declarations.TryGetValue(schema.Id, out var declaration);
                    var fields = ComponentSchemaRegistry.PlanMigration(pool.Schema, schema, declaration, out var droppedField);
                    var next = CreatePool(schema);
                    replacements.Add(new(pool, next, fields, droppedField));
                    added.Add(pool = next);
                }
            }
            else
            {
                if (!allowPoolChanges) throw new InvalidDataException($"Simulation schema '{schema.Id}' was added during code reload.");
                if (declarations?.ContainsKey(schema.Id) == true)
                    throw new InvalidDataException($"Simulation schema '{schema.Id}' has a migration but no accepted version.");
                added.Add(pool = CreatePool(schema));
            }
            byId.Add(schema.Id, pool);
        }
        var dropped = _active.Pools.Where(pool => !byId.ContainsKey(pool.Schema.Id)).ToArray();
        if (!allowPoolChanges && dropped.Length != 0)
            throw new InvalidDataException($"Simulation schema '{dropped[0].Schema.Id}' was removed during code reload.");
        return new CatalogChange(this, added.Count == 0 && dropped.Length == 0 ? _active : new Catalog(byId),
            added.ToArray(), dropped, replacements.ToArray());
    }

    internal sealed class Catalog(Dictionary<string, DynamicComponentPool> byId)
    {
        internal readonly Dictionary<string, DynamicComponentPool> ById = byId;
        internal readonly DynamicComponentPool[] Pools = byId.Values.ToArray();
        internal DynamicComponentPool Resolve(string id) => ById.TryGetValue(id, out var pool) ? pool :
            throw new InvalidDataException($"Unknown dynamic component '{id}'.");
    }

    internal readonly record struct PoolMigration(DynamicComponentPool Previous, DynamicComponentPool Next,
        ComponentFieldTransfer[] Fields, string? DroppedField);

    // Only the active catalog sees Dragon hooks. Detached replacements preserve dense membership;
    // dropping a pool requires it to be empty.
    internal sealed class CatalogChange(DynamicComponentStorage storage, Catalog target,
        DynamicComponentPool[] added, DynamicComponentPool[] dropped, PoolMigration[] migrations) : IDisposable
    {
        private bool _published;
        internal DynamicComponentPool[] Retired { get; } = [.. dropped, .. migrations.Select(static migration => migration.Previous)];
        internal DynamicComponentPool[] Dropped => dropped;
        internal DynamicComponentPool Resolve(string id) => target.Resolve(id);
        internal void RequireNoLiveFieldLoss()
        {
            foreach (var migration in migrations)
                if (migration.DroppedField is { } field && migration.Previous.Count != 0)
                    throw new InvalidDataException($"Cannot drop field '{field}' from '{migration.Previous.Schema.Id}': it still has live rows.");
        }
        internal void PrepareForPublication(CancellationToken cancellationToken)
        {
            storage.ThrowIfDisposed();
            foreach (var pool in added) pool.ResizeMapping(storage._world.Capacity);
            foreach (var migration in migrations)
                migration.Next.CopyRowsFrom(migration.Previous, migration.Fields, cancellationToken);
        }
        internal void RequirePreparedMembership()
        {
            foreach (var migration in migrations)
                if (migration.Previous.Count != migration.Next.Count)
                    throw new InvalidOperationException($"Component '{migration.Previous.Schema.Id}' membership changed after Ready.");
        }
        internal void Publish() { storage._active = target; _published = true; }
        public void Dispose()
        {
            foreach (var pool in _published ? Retired : added) pool.Clear();
        }
    }

    internal EcsDefaultWorld CommandWorld => _world;
    internal int CommandAnchorCount => _anchors.Count;
    internal int CommandAnchorCapacity => _anchors.Capacity - 1;
    internal int CommandDynamicCount(int entity) { ValidateEntity(entity); return _counts[entity]; }

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
        foreach (var pool in _active.Pools) pool.ResizeMapping(newSize);
    }

    public void OnReleaseDelEntityBuffer(ReadOnlySpan<int> buffer)
    {
        foreach (int entity in buffer)
        {
            foreach (var pool in _active.Pools) pool.RemoveReleasedEntity(entity);
            _counts[entity] = 0;
        }
    }

    public void OnWorldDestroy()
    {
        _disposed = true;
        foreach (var pool in _active.Pools) pool.Clear();
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
