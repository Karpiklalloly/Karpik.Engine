using System.Composition;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Shared.Modding.Bindings;

/// <summary>
/// Prepares one access revision over Simulation-owned component storage. The DI-created root owns
/// the storage; child revisions own only their capabilities, query listeners and command scratch.
/// Creation, activation and retirement are maintenance operations performed at a closed host barrier.
/// </summary>
[Export(typeof(ComponentQueryPreparer))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class ComponentQueryPreparer : IDisposable
{
    private readonly PersistentStorage _storage;
    private readonly ComponentCallbackScope _scope;
    private readonly bool _ownsStorage;
    private readonly List<PreparedComponentQuery> _queries = new();
    private StructuralCommandBuffer? _commands;
    private StructuralCommandOptions? _commandOptions;
    private bool _retired;
    private bool _disposed;

    public ComponentQueryPreparer(EcsDefaultWorld world, ClrComponentBindings clr,
        ComponentSchemaRegistry schemas, ComponentCallbackScope scope, ComponentPointerRegistry? pointers = null)
    {
        _storage = new PersistentStorage(world, clr, schemas, pointers);
        _scope = scope;
        _ownsStorage = true;
        _storage.Revisions.Add(this);
    }

    private ComponentQueryPreparer(PersistentStorage storage)
    {
        _storage = storage;
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

    /// <summary>Enables prepared callbacks. Call only while the host excludes all frame access.</summary>
    public void ActivateRevision()
    {
        RequireRevision();
        _scope.Activate();
    }

    /// <summary>Revokes this revision without destroying the persistent world or component pools.</summary>
    public void RetireRevision()
    {
        if (_retired) return;
        _retired = true;
        _scope.Dispose();
        // Remove first so a failed listener cleanup cannot trap Simulation teardown in a retry loop.
        _storage.Revisions.Remove(this);
        List<Exception>? errors = null;
        foreach (PreparedComponentQuery query in _queries)
        {
            try { query.Dispose(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        _queries.Clear();
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
                            pool = _storage.Pointers is null
                                ? binding.Bind(_storage.World)
                                : binding.Bind(_storage.World, _storage.Pointers);
                            _storage.Native.Add(type!, pool);
                        }
                    }
                    else pool = _storage.Dynamic.Resolve(entry.ComponentId);
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

    public StructuralCommandBuffer GetCommands(StructuralCommandOptions options)
    {
        RequireRevision();
        ArgumentNullException.ThrowIfNull(options);
        if (_storage.Dynamic is null)
            throw new InvalidOperationException("Component queries require initialized storage.");
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
        ObjectDisposedException.ThrowIf(_retired || _disposed, this);
        _storage.RequireAlive();
    }

    private sealed class PersistentStorage(EcsDefaultWorld world, ClrComponentBindings clr,
        ComponentSchemaRegistry schemas, ComponentPointerRegistry? pointers) : IDisposable
    {
        internal readonly EcsDefaultWorld World = world;
        internal readonly ClrComponentBindings Clr = clr;
        internal readonly ComponentPointerRegistry? Pointers = pointers;
        internal readonly Dictionary<Type, IComponentPoolAccess> Native = new();
        internal readonly List<ComponentQueryPreparer> Revisions = new();
        internal DynamicComponentStorage? Dynamic;
        private bool _disposed;

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
