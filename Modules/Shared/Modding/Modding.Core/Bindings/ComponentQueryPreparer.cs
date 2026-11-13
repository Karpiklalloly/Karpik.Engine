using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

using DCFApixels.DragonECS;

namespace Karpik.Engine.Shared.Modding.Bindings;

[Export(typeof(ComponentQueryPreparer))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class ComponentQueryPreparer(EcsDefaultWorld world, ClrComponentBindings clr,
    ComponentSchemaRegistry schemas, ComponentCallbackScope scope, ComponentPointerRegistry? pointers = null) : IDisposable
{
    private readonly Dictionary<Type, IComponentPoolAccess> _native = new();
    private readonly List<PreparedComponentQuery> _queries = new();
    private DynamicComponentStorage? _dynamic;
    private bool _disposed;
    private StructuralCommandBuffer? _commands;
    private StructuralCommandOptions? _commandOptions;
    private readonly ComponentPointerRegistry? _pointers = pointers;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dynamic ??= new DynamicComponentStorage(world, schemas, _pointers);
        _pointers?.Freeze();
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
                            var binding = clr.ResolveBinding(entry.ComponentId);
                            pool = _pointers is null ? binding.Bind(world) : binding.Bind(world, _pointers);
                            _native.Add(type!, pool);
                        }
                    }
                    else pool = _dynamic.Resolve(entry.ComponentId);
                }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
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

    public StructuralCommandBuffer GetCommands(StructuralCommandOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        if (_dynamic is null) throw new InvalidOperationException("Component queries require initialized storage.");
        if (_commands is not null && options != _commandOptions) throw new InvalidOperationException("Command limits cannot change within a Simulation.");
        if (_commands is null)
        {
            var commands = new StructuralCommandBuffer(world, scope, _dynamic, options);
            _commandOptions = options; _commands = commands;
        }
        return _commands;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var query in _queries) query.Dispose();
        _queries.Clear(); _native.Clear(); _commands?.Dispose(); _dynamic?.Dispose();
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
