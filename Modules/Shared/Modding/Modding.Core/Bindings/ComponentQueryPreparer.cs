using System.Composition;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Shared.Modding.Bindings;

/// <summary>
/// Prepares one access domain over Simulation-owned storage. Children own their capabilities
/// and query listeners; sequential Lua domains share one command arena.
/// Creation, activation and retirement are maintenance operations performed at a closed host barrier.
/// </summary>
[Export(typeof(ComponentQueryPreparer))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class ComponentQueryPreparer : IDisposable
{
    private readonly PersistentStorage _storage;
    private readonly ComponentCallbackScope _scope;
    private readonly bool _ownsStorage;
    private readonly bool _sequential;
    private StagedCatalog? _staged;
    private readonly List<PreparedComponentQuery> _queries = new();
    private List<PreparedComponentHandle>? _wireClaims;
    private StructuralCommandBuffer? _commands;
    private StructuralCommandOptions? _commandOptions;
    private bool _revoked, _retired;
    private bool _disposed;

    public ComponentQueryPreparer(EcsDefaultWorld world, ClrComponentBindings clr,
        ComponentSchemaRegistry schemas, ComponentCallbackScope scope, ComponentPointerRegistry? pointers = null)
    {
        _storage = new PersistentStorage(world, clr, schemas, pointers);
        _scope = scope;
        _ownsStorage = true;
        _storage.Revisions.Add(this);
    }

    private ComponentQueryPreparer(PersistentStorage storage, bool sequential = false, StagedCatalog? staged = null)
    {
        _storage = storage;
        _sequential = sequential;
        _staged = staged;
        _scope = new ComponentCallbackScope(active: false);
        _storage.Revisions.Add(this);
    }

    public ComponentCallbackScope CallbackScope => _scope;

    /// <summary>Creates an inactive access revision without copying or replacing ECS storage.</summary>
    public ComponentQueryPreparer CreateRevision()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _storage.RequireAlive();
        if (_storage.Dynamic is null)
            throw new InvalidOperationException("Component revisions require initialized storage.");
        return new ComponentQueryPreparer(_storage);
    }

    internal ComponentQueryPreparer CreateSequentialRevision()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _storage.RequireAlive();
        if (_storage.Dynamic is null || _storage.Staged is not null)
            throw new InvalidOperationException("Sequential domains require initialized storage without a pending catalog.");
        return new ComponentQueryPreparer(_storage, sequential: true);
    }

    internal StagedCatalog PrepareCatalog(ReadOnlySpan<ComponentSchema> schemas, ComponentQueryPreparer[] retiring,
        bool allowPoolChanges, IReadOnlyDictionary<string, ComponentMigration>? migrations = null)
    {
        if (!_ownsStorage) throw new InvalidOperationException("Only the Simulation root can prepare a catalog.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        _storage.RequireAlive();
        if (_storage.Staged is not null) throw new InvalidOperationException("A component catalog is already staged.");
        var dynamic = _storage.Dynamic ?? throw new InvalidOperationException("Component storage is not initialized.");
        if (migrations is not null && _storage.World.DelEntBufferCount != 0)
            throw new InvalidDataException("Schema reload requires the host to drain pending ECS deletions.");
        var changes = dynamic.PrepareCatalog(ComponentSchemaRegistry.Canonicalize(schemas), allowPoolChanges, migrations);
        // Live selection and coordinated schema reload can stage dynamic pool changes; legacy reload keeps lazy CLR binding.
        var stage = new StagedCatalog(_storage, changes, retiring, requireInitializedClrPools: allowPoolChanges,
            hostDrainsDeletes: migrations is not null);
        _storage.Staged = stage;
        try { stage.ValidateRetirement(); return stage; }
        catch { stage.Dispose(); throw; }
    }

    /// <summary>Enables prepared callbacks. Call only while the host excludes all frame access.</summary>
    public void ActivateRevision()
    {
        RequireRevision();
        _scope.Activate();
        _staged = null;
    }

    internal void RevokeRevision()
    {
        if (_revoked) return;
        _revoked = true;
        _scope.Dispose();
    }

    /// <summary>Revokes this revision without destroying the persistent world or component pools.</summary>
    public void RetireRevision()
    {
        if (_retired) return;
        _retired = true;
        RevokeRevision();
        _staged = null;
        // Remove first so a failed listener cleanup cannot trap Simulation teardown in a retry loop.
        _storage.Revisions.Remove(this);
        List<Exception>? errors = null;
        foreach (PreparedComponentQuery query in _queries)
        {
            try { query.Dispose(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        _queries.Clear();
        _wireClaims?.Clear();
        try { _commands?.Dispose(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        _commands = null;
        // Tracking is maintenance-only, bounded by live revisions, and adds no hot-path checks.
        if (errors is not null) throw new AggregateException("Component revision cleanup failed.", errors);
    }

    public void Initialize()
    {
        RequireRevision();
        _storage.Initialize();
    }

    public PreparedComponentQuery Prepare(ComponentQueryDefinition definition, string owner)
    {
        RequireRevision();
        if (_storage.Dynamic is null)
            throw new InvalidOperationException("Component queries require frozen schemas and initialized storage.");
        if (_storage.Staged is not null && _staged is null)
            throw new InvalidOperationException("New bindings cannot borrow an active catalog during a staged change.");
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        var handles = new Dictionary<string, PreparedComponentHandle>(StringComparer.Ordinal);
        var inc = Bind(definition.Inc); var exc = Bind(definition.Exc); Bind(definition.Opt);
        var query = new PreparedComponentQuery(_storage.World, _scope, owner, handles, inc, exc);
        _queries.Add(query);
        return query;

        PreparedComponentHandle[] Bind(ComponentQueryEntry[] entries)
        {
            var group = new PreparedComponentHandle[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                ComponentQueryEntry entry = entries[i];
                Type? type = null;
                IComponentPoolAccess pool;
                try
                {
                    if (_storage.Clr.TryResolveType(entry.ComponentId, out type))
                    {
                        if (!_storage.Native.TryGetValue(type!, out pool!))
                        {
                            var binding = _storage.Clr.ResolveBinding(entry.ComponentId);
                            _staged?.RequireBindableClrPool(type!);
                            pool = _storage.Pointers is null
                                ? binding.Bind(_storage.World)
                                : binding.Bind(_storage.World, _storage.Pointers);
                            _storage.Native.Add(type!, pool);
                        }
                    }
                    else pool = _staged is { } staged ? staged.Resolve(entry.ComponentId) : _storage.Dynamic.Resolve(entry.ComponentId);
                }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
                {
                    throw new InvalidDataException($"System '{owner}', query '{definition.Name}', component '{entry.ComponentId}': {error.Message}", error);
                }
                group[i] = new PreparedComponentHandle(entry.ComponentId, type, entry.Readonly, pool, _scope, owner);
                if (!handles.TryAdd(entry.ComponentId, group[i]))
                    throw new InvalidDataException($"System '{owner}', query '{definition.Name}': duplicate component '{entry.ComponentId}'.");
            }
            return group;
        }
    }

    internal PreparedComponentHandle BorrowWireComponent(string componentId, string owner)
    {
        RequireRevision();
        if (_ownsStorage || (_storage.Staged is not null && _staged is null))
            throw new InvalidOperationException("A wire claim requires its own access revision over the prepared catalog.");
        var pool = _staged?.Resolve(componentId) ?? _storage.Dynamic!.Resolve(componentId);
        var handle = new PreparedComponentHandle(componentId, null, readOnly: false, pool, _scope, owner);
        (_wireClaims ??= []).Add(handle);
        return handle;
    }

    internal void RequireWireWorld(EcsWorld world)
    {
        RequireRevision();
        if (!ReferenceEquals(world, _storage.World))
            throw new InvalidOperationException("The wire projection belongs to another Simulation world.");
    }

    public StructuralCommandBuffer GetCommands(StructuralCommandOptions options)
    {
        RequireRevision();
        ArgumentNullException.ThrowIfNull(options);
        if (_storage.Dynamic is null)
            throw new InvalidOperationException("Component queries require initialized storage.");
        if (_sequential) return _storage.GetCommands(options);
        if (_commands is not null && options != _commandOptions)
            throw new InvalidOperationException("Command limits cannot change within a revision.");
        if (_commands is null)
        {
            _commands = new StructuralCommandBuffer(_storage.World, _scope, _storage.Dynamic, options);
            _commandOptions = options;
        }
        return _commands;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsStorage)
        {
            // Simulation teardown invalidates every still-live child before destroying shared pools.
            List<Exception>? errors = null;
            while (_storage.Revisions.Count != 0)
            {
                try { _storage.Revisions[^1].RetireRevision(); }
                catch (Exception error) { (errors ??= []).Add(error); }
            }
            try { _storage.Dispose(); }
            catch (Exception error) { (errors ??= []).Add(error); }
            if (errors is not null) throw new AggregateException("Simulation component cleanup failed.", errors);
        }
        else RetireRevision();
    }

    private void RequireRevision()
    {
        ObjectDisposedException.ThrowIf(_revoked || _disposed, this);
        _storage.RequireAlive();
    }

    internal sealed class StagedCatalog(PersistentStorage storage, DynamicComponentStorage.CatalogChange change,
        ComponentQueryPreparer[] retiring, bool requireInitializedClrPools, bool hostDrainsDeletes) : IDisposable
    {
        private bool _ready, _disposed;
        private long _readyWorldVersion;

        internal ComponentQueryPreparer CreateSequentialRevision()
        {
            RequirePreparing();
            return new ComponentQueryPreparer(storage, sequential: true, staged: this);
        }

        internal IComponentPoolAccess Resolve(string id)
        {
            RequirePreparing();
            return change.Resolve(id);
        }

        internal void RequireBindableClrPool(Type type)
        {
            if (!requireInitializedClrPools) return;
            var world = storage.World;
            // The Type lookup itself declares an ID when called for an undeclared component.
            if (world.IsComponentTypeDeclared(type) && world.TryFindPoolInstance(type, out var pool))
            {
                var poolType = pool.GetType();
                if (poolType.IsGenericType && poolType.GetGenericTypeDefinition() == typeof(EcsPool<>)) return;
            }
            throw new InvalidDataException("Live mod queries require the host to initialize the CLR component's standard pool with GetPool<T>() during startup.");
        }

        internal void ValidateRetirement()
        {
            RequirePreparing();
            RequireQuiescentCallbacks();
            RequireHostDrainedDeletes();
            foreach (var revision in storage.Revisions)
            {
                if (change.Retired.Length == 0 || Array.IndexOf(retiring, revision) >= 0) continue;
                if (revision._wireClaims is { } claims)
                    foreach (var claim in claims)
                        foreach (var pool in change.Retired)
                            if (ReferenceEquals(claim.Pool, pool))
                                throw new InvalidDataException($"Cannot retire '{pool.Schema.Id}': an active wire projection still borrows it.");
                foreach (var query in revision._queries)
                    foreach (var pool in change.Retired)
                        if (query.Components.TryGetValue(pool.Schema.Id, out var handle) && ReferenceEquals(handle.Pool, pool))
                            throw new InvalidDataException($"Cannot retire '{pool.Schema.Id}': '{query.Owner}' still has a prepared binding.");
            }
            change.RequireNoLiveFieldLoss();
            if (change.Dropped.Length == 0) return;
            // Preserve existing Apply behavior: finish accepted deletions, never delete a live row to drop its pool.
            if (!hostDrainsDeletes && storage.World.DelEntBufferCount != 0)
            {
                try { storage.World.ReleaseDelEntityBufferAll(); }
                catch (Exception error)
                {
                    throw new SimulationCodeReloadCleanupException("ECS deletion playback may have partially mutated the world.", error);
                }
            }
            if (storage.World.DelEntBufferCount != 0) throw new InvalidOperationException("ECS deletion is not quiescent.");
            foreach (var pool in change.Dropped)
                if (pool.Count != 0) throw new InvalidDataException($"Cannot remove '{pool.Schema.Id}': it still has live rows.");
        }

        internal void PreparePublication(CancellationToken cancellationToken)
        {
            ValidateRetirement();
            change.PrepareForPublication(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _readyWorldVersion = storage.World.Version;
            _ready = true;
        }

        // Defensive checks, not a replacement for the host holding quiescence across Ready/Commit.
        internal void RequireReady()
        {
            if (!_ready || _disposed) throw new InvalidOperationException("Component catalog is not ready.");
            RequireQuiescentCallbacks();
            RequireHostDrainedDeletes();
            if (storage.World.Version != _readyWorldVersion)
                throw new InvalidOperationException("The ECS world changed after the catalog became ready.");
            // Adding a second dynamic component to an already anchored entity need not change World.Version.
            foreach (var pool in change.Dropped)
                if (pool.Count != 0) throw new InvalidOperationException($"Component '{pool.Schema.Id}' acquired live rows after Ready.");
            change.RequirePreparedMembership();
        }

        private void RequireHostDrainedDeletes()
        {
            if (hostDrainsDeletes && storage.World.DelEntBufferCount != 0)
                throw new InvalidDataException("Schema reload requires the host to drain pending ECS deletions.");
        }

        private void RequireQuiescentCallbacks()
        {
            if (storage.Commands is { IsActive: true })
                throw new InvalidOperationException("Structural playback is not quiescent.");
            foreach (var revision in storage.Revisions)
                if (revision._scope.IsEntered || revision._commands is { IsActive: true })
                    throw new InvalidOperationException("Component callbacks are not quiescent.");
        }

        internal void Publish() => change.Publish();

        private void RequirePreparing()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ready) throw new InvalidOperationException("The component catalog is closed to preparation.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            storage.Staged = null;
            change.Dispose();
        }
    }

    internal sealed class PersistentStorage(EcsDefaultWorld world, ClrComponentBindings clr,
        ComponentSchemaRegistry schemas, ComponentPointerRegistry? pointers) : IDisposable
    {
        internal readonly EcsDefaultWorld World = world;
        internal readonly ClrComponentBindings Clr = clr;
        internal readonly ComponentPointerRegistry? Pointers = pointers;
        internal readonly Dictionary<Type, IComponentPoolAccess> Native = new();
        internal readonly List<ComponentQueryPreparer> Revisions = new();
        internal DynamicComponentStorage? Dynamic;
        internal StagedCatalog? Staged;
        internal StructuralCommandBuffer? Commands;
        private StructuralCommandOptions? _options;
        private bool _disposed;

        internal StructuralCommandBuffer GetCommands(StructuralCommandOptions options)
        {
            if (Commands is null)
            {
                Commands = new StructuralCommandBuffer(World, null, Dynamic!, options);
                _options = options;
            }
            else if (options != _options) throw new InvalidOperationException("Sequential command limits cannot change within a Simulation.");
            return Commands;
        }

        internal void Initialize()
        {
            RequireAlive();
            Dynamic ??= new DynamicComponentStorage(World, schemas, Pointers);
            Pointers?.Freeze();
        }

        internal void RequireAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Native.Clear();
            Staged?.Dispose();
            Commands?.Dispose();
            Commands = null;
            Dynamic?.Dispose();
            Dynamic = null;
        }
    }

    public static EcsComponentAccessDescriptor[] InferAccesses(ReadOnlySpan<PreparedComponentQuery> queries)
    {
        var accesses = new Dictionary<EcsComponentAccessKey, EcsAccessMode>();
        foreach (PreparedComponentQuery query in queries)
        {
            foreach (PreparedComponentHandle handle in query.Components.Values)
            {
                var key = handle.ClrType is Type type ? EcsComponentAccessKey.ForClr(type) : EcsComponentAccessKey.ForDynamic(handle.ComponentId);
                Merge(key, handle.Readonly ? EcsAccessMode.Read : EcsAccessMode.Write);
                if (handle.ClrType is null && !handle.Readonly)
                    Merge(EcsComponentAccessKey.ForClr(typeof(DynamicComponentAnchor)), EcsAccessMode.Write);
            }
        }
        return accesses.Select(pair => new EcsComponentAccessDescriptor(pair.Key, pair.Value)).ToArray();

        void Merge(EcsComponentAccessKey key, EcsAccessMode mode)
        {
            if (mode == EcsAccessMode.Write || !accesses.ContainsKey(key)) accesses[key] = mode;
        }
    }
}
