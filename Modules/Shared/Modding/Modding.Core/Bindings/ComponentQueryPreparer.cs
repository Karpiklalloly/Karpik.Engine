using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

using DCFApixels.DragonECS;

namespace Karpik.Engine.Shared.Modding.Bindings;

[Export(typeof(ComponentQueryPreparer))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class ComponentQueryPreparer(EcsDefaultWorld world, ClrComponentBindings clr,
    ComponentSchemaRegistry schemas, ComponentCallbackScope scope) : IDisposable
{
    private readonly Dictionary<Type, IComponentPoolAccess> _native = new();
    private readonly List<PreparedComponentQuery> _queries = new();
    private DynamicComponentStorage? _dynamic;
    private bool _disposed;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dynamic ??= new DynamicComponentStorage(world, schemas);
    }

    public PreparedComponentQuery Prepare(ComponentQueryDefinition definition, string owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dynamic is null) throw new InvalidOperationException("Component queries require frozen schemas and initialized storage.");
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        var handles = new Dictionary<string, PreparedComponentHandle>(StringComparer.Ordinal);
        var inc = Bind(definition.Inc); var exc = Bind(definition.Exc); Bind(definition.Opt);
        var query = new PreparedComponentQuery(world, scope, owner, handles, inc, exc);
        _queries.Add(query); return query;

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
                    if (clr.TryResolveType(entry.ComponentId, out type))
                    {
                        if (!_native.TryGetValue(type!, out pool!))
                        {
                            pool = clr.ResolveBinding(entry.ComponentId).Bind(world); _native.Add(type!, pool);
                        }
                    }
                    else pool = _dynamic.Resolve(entry.ComponentId);
                }
                catch (InvalidDataException error)
                {
                    throw new InvalidDataException($"System '{owner}', query '{definition.Name}', component '{entry.ComponentId}': {error.Message}", error);
                }
                group[i] = new PreparedComponentHandle(entry.ComponentId, type, entry.Readonly, pool, scope, owner);
                if (!handles.TryAdd(entry.ComponentId, group[i]))
                    throw new InvalidDataException($"System '{owner}', query '{definition.Name}': duplicate component '{entry.ComponentId}'.");
            }
            return group;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var query in _queries) query.Dispose();
        _queries.Clear(); _native.Clear(); _dynamic?.Dispose();
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
