using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Security.Cryptography;
using DCFApixels.DragonECS;
using System.Composition;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua.Systems;
using Karpik.Engine.Shared.Network.Core;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(ISimulationPreparation))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class LuaSimulationPreparation(
    IModsRegistry registry,
    IModsLifecycleManager lifecycle,
    Application application,
    ISystemRuntimeRegistry systems,
    Time time,
    LuaComponentRegistry components,
    ComponentSchemaRegistry schemaRegistry,
    ComponentQueryPreparer queryPreparer,
    ComponentCallbackScope callbackScope,
    ScriptSystemOptions options,
    ILogger<IScriptLoader> logger,
    MainThreadScheduler? mainThreadScheduler = null,
    IModContentParticipant? contentPreparation = null) : ISimulationPreparation, ISimulationLiveModParticipant, IDisposable
#if DEBUG
    , ISimulationSchemaReloadParticipant
#endif
{
    private readonly IModsLifecycleManager _lifecycle = lifecycle;
    private readonly ComponentQueryPreparer _queryPreparer = queryPreparer;
    private readonly ILogger<IScriptLoader> _logger = logger;
    private ExecutionSide _side;
    private Revision? _current;
    private Candidate? _pending;
    private int _ownerThread;
    private bool _preparing, _faulted, _disposed;

    internal ModStartupDependencies? AcceptedComposition => _current?.Composition;
    internal IRevisionReplicaProjection? AcceptedWireProjection => _current?.Wire;
    internal IRevisionReplicaProjection? PendingWireProjection => _pending?.Wire;
    internal int PendingWireRowBytes => _pending?.Wire?.MaxRowBytes ?? 0;

    public ReadOnlyMemory<ISystemUpdate> ActiveSystems => _current?.Systems ?? ReadOnlyMemory<ISystemUpdate>.Empty;

    public async ValueTask PrepareAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ownerThread = Environment.CurrentManagedThreadId;
        cancellationToken.ThrowIfCancellationRequested();
        _side = application.ApplicationSide switch
        {
            Side.Client => ExecutionSide.Client,
            Side.Server => ExecutionSide.Server,
            _ => throw new InvalidOperationException("Unsupported application side.")
        };
        var startup = registry is ModsRegistry mods
            ? mods.StartupDependencies ?? mods.ResolveStartupDependencies(_side == ExecutionSide.Client ? AssetTarget.Client : AssetTarget.Server,
                FrozenDictionary<string, ModCookedAvailability>.Empty, cancellationToken)
            : null;
        IReadOnlyList<ModDefinition> definitions = startup is not null ? startup.ScriptedMods :
            registry.GetMods().OrderBy(definition => definition.MetaData.Id, StringComparer.Ordinal).ToArray();
        await _lifecycle.Load(definitions, _side, cancellationToken);
        await OnOwnerAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_queryPreparer.CallbackScope, callbackScope))
                throw new InvalidOperationException("Component preparation and Simulation must share the host binding scope.");
            var owners = _lifecycle.Containers.ToArray();
            var vms = Array.ConvertAll(owners, static owner => new Vm(owner));
            schemaRegistry.ValidateAndFreeze(ReadSchemas(vms));
            _queryPreparer.Initialize();
            components.Seal();
            foreach (Vm vm in vms)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Bind(vm, catalog: null);
                vm.Bindings?.ActivateRevision();
            }
            var wire = PrepareWire(vms, catalog: null);
            wire?.Domain.ActivateRevision();
            _current = new Revision(vms, startup, wire);
            for (int i = 0; i < _current.Systems.Length; i++)
                systems.AddInstance(_current.Systems[i], _current.Descriptors[i]);
            return true;
        });
    }

    public ValueTask<ISimulationCodeReloadCandidate> PrepareReloadAsync(CancellationToken cancellationToken)
        => PrepareCandidateAsync(target: null, cancellationToken);

    ModContentRefreshCapture? ISimulationLiveModParticipant.CreateLiveModRefreshCapture(Action<ContentDiagnostic> report) =>
        (contentPreparation as LuaModSetPreparation)?.CreateLiveRefreshCapture(
            () => Volatile.Read(ref _current)?.Composition ?? throw new InvalidOperationException("No accepted live composition."), report);

    async ValueTask<ISimulationCodeReloadCandidate?> ISimulationLiveModParticipant.PrepareLiveModAsync(
        ContentReloadCoordinator? coordinator, IReadOnlyCollection<string> selectedIds, Action<ContentDiagnostic> report,
        CancellationToken cancellationToken)
    {
        RequireOwner();
        if (coordinator is null || contentPreparation is not LuaModSetPreparation content || AcceptedComposition is not { } basis)
            throw new NotSupportedException("Live selection requires accepted Lua and Content roots.");
        var staged = await content.PrepareLiveSelectionAsync(coordinator, basis, selectedIds, report, cancellationToken).ConfigureAwait(false);
        if (staged is not { } next) return null;
        try
        {
            var pending = await OnOwnerAsync(() => PrepareLiveAsync(basis, next.Composition, cancellationToken));
            return new LiveCandidate(await pending, next.Update);
        }
        catch (Exception preparationError)
        {
            try { next.Update.Dispose(); }
            catch (Exception cleanupError)
            {
                throw new SimulationCodeReloadCleanupException("Failed to abort staged mod Content.",
                    new AggregateException(preparationError, cleanupError));
            }
            throw;
        }
    }

    // Content publishes last so its coordinator cannot reopen admission before the Lua composition is accepted.
    private sealed class LiveCandidate(ISimulationCodeReloadCandidate lua, ContentReloadCoordinator.PreparedModUpdate content)
        : ISimulationCodeReloadCandidate
    {
        public ReadOnlyMemory<ISystemUpdate> Systems => lua.Systems;
        public ReadOnlyMemory<EcsUpdateSystemDescriptor> Descriptors => lua.Descriptors;
        public void PrepareActivation(CancellationToken cancellationToken) => lua.PrepareActivation(cancellationToken);
        public ReadOnlyMemory<byte> SharedSchemaProof => lua.SharedSchemaProof;
        public int? MaximumModReplicaRowBytes => lua.MaximumModReplicaRowBytes;
        public void Activate()
        {
            lua.Activate();
            if (!content.TryCommitAtBarrier()) throw new InvalidOperationException("The reserved mod Content basis could not commit.");
        }
        public void RetirePrevious() => lua.RetirePrevious();
        public void Dispose()
        {
            try { lua.Dispose(); }
            finally { content.Dispose(); } // The cold Content coordinator owns retirement; never await claims under the lease.
        }
    }

    /// <summary>The host holds its owner-thread gameplay barrier from prepare through retirement/start.</summary>
    internal ValueTask<ISimulationCodeReloadCandidate> PrepareLiveAsync(ModStartupDependencies expectedBasis,
        ModStartupDependencies target, CancellationToken cancellationToken)
    {
        RequireOwner();
        if (_current?.Composition is not { } basis || !ReferenceEquals(expectedBasis, basis))
            throw new InvalidOperationException("The live mod selection basis is no longer active.");
        if (target.Side != basis.Side) throw new InvalidDataException("The live mod selection targets another side.");
        if (_lifecycle is not ModsLifecycleManager)
            throw new NotSupportedException("This mod lifecycle cannot prepare a pinned live selection.");
        return PrepareCandidateAsync(target, cancellationToken);
    }

#if DEBUG
    bool ISimulationSchemaReloadParticipant.SupportsSchemaReload =>
        AcceptedComposition is not null && _lifecycle is ModsLifecycleManager && LuaSourceCapture.IsSupported;

    async ValueTask<IPreparedSchemaReload> ISimulationSchemaReloadParticipant.PrepareSchemaReloadAsync(
        Func<string, bool> admitLocalArtifact, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(admitLocalArtifact);
        RequireOwner();
        if (AcceptedComposition is null || _lifecycle is not ModsLifecycleManager)
            throw new NotSupportedException("No accepted pinned Lua composition.");
        return (IPreparedSchemaReload)await PrepareCandidateAsync(null, token, schemaReload: true,
            checkCapture: definitions =>
            {
                if (!admitLocalArtifact(SchemaArtifactDigest(_side, definitions.Select(static definition =>
                    (definition.MetaData.Id, definition.ScriptSource!)))))
                    throw new UnauthorizedAccessException("The exact local Lua artifact is not admitted for this session.");
            });
    }

    internal static string SchemaArtifactDigest(ExecutionSide side, IEnumerable<(string Id, ModScriptSource Source)> scripts) =>
        Convert.ToHexString(HashRecipe(writer =>
        {
            writer.Write("Karpik.LuaSchema.Local.v1");
            writer.Write((int)side);
            var definitions = scripts.OrderBy(static entry => entry.Id, StringComparer.Ordinal).ToArray();
            writer.Write(definitions.Length);
            foreach (var (id, source) in definitions)
            {
                writer.Write(id);
                writer.Write(source.SharedIdentity);
                writer.Write(source.SideIdentity);
            }
        }));
#endif

    private static byte[] HashRecipe(Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        write(writer);
        writer.Flush();
        return SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    private static byte[] ProveSharedSchema(Revision revision) => HashRecipe(writer =>
    {
        writer.Write("Karpik.LuaSchema.Shared.v1");
        var shared = revision.Vms.Where(static vm => vm.Container.ModDefinition.ScriptSource?.HasSharedFiles == true)
            .OrderBy(static vm => vm.Container.ModDefinition.MetaData.Id, StringComparer.Ordinal).ToArray();
        writer.Write(shared.Length);
        foreach (var vm in shared)
        {
            var definition = vm.Container.ModDefinition;
            writer.Write(definition.MetaData.Id);
            writer.Write(definition.ScriptSource!.SharedIdentity);
            var runtime = (LuaScriptRuntime)vm.Container.Runtime;
            var schemas = ComponentSchemaRegistry.Canonicalize(runtime.Components.AsSpan(0, runtime.SharedSchemaCount));
            writer.Write(schemas.Length);
            foreach (var schema in schemas)
            {
                writer.Write(schema.Id);
                writer.Write(schema.Version);
                Fields(schema.Fields, 0);
                if (runtime.Migrations is not { } migrations || !migrations.TryGetValue(schema.Id, out var migration))
                { writer.Write(false); continue; }
                writer.Write(true);
                writer.Write(migration.FromVersion);
                writer.Write(migration.Fields.Count);
                foreach (var (name, rule) in migration.Fields.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
                {
                    writer.Write(name);
                    writer.Write(rule.Source is not null);
                    if (rule.Source is { } from) { writer.Write(from); writer.Write(rule.WidenI32); }
                    else if (rule.DefaultValue is bool flag) writer.Write(flag);
                    else writer.Write(Convert.ToDouble(rule.DefaultValue, System.Globalization.CultureInfo.InvariantCulture));
                    // All accepted integer defaults fit exactly in a Lua double. The schema binds the scalar type.
                }
            }
        }
        var wire = revision.Wire;
        writer.Write(wire is not null);
        if (wire is not null) writer.Write(wire.Digest);

        void Fields(SchemaField[] fields, int depth)
        {
            if (depth >= 64) throw new InvalidDataException("Shared schema proof exceeds its nesting limit.");
            writer.Write(fields.Length);
            foreach (var field in fields)
            {
                writer.Write(field.Name);
                if (writer.BaseStream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Shared schema proof exceeds its byte budget.");
                Value(field.Value, field.Kind, depth + 1);
            }
        }
        void Value(SchemaValue? node, SchemaScalarKind scalar, int depth)
        {
            writer.Write((byte)(node?.NodeKind ?? SchemaValueKind.Scalar));
            if (node is null) writer.Write((byte)scalar);
            else switch (node.NodeKind)
            {
                case SchemaValueKind.Scalar or SchemaValueKind.Enum: writer.Write((byte)node.ScalarKind); break;
                case SchemaValueKind.Pointer: writer.Write(node.PointerTypeId!); break;
                case SchemaValueKind.Struct: Fields(node.Fields, depth); break;
                case SchemaValueKind.Fixed: writer.Write(node.Length); Value(node.Element!, default, depth + 1); break;
            }
        }
    });

    /// <summary>Prepare fresh code for the accepted composition under the host's closed gameplay barrier.</summary>
    internal ValueTask<ISimulationCodeReloadCandidate> PrepareSchemaReloadAsync(ModStartupDependencies expectedBasis,
        CancellationToken cancellationToken)
    {
#if DEBUG
        RequireOwner();
        if (_current?.Composition is not { } basis || !ReferenceEquals(expectedBasis, basis))
            throw new InvalidOperationException("The schema reload basis is no longer active.");
        if (_lifecycle is not ModsLifecycleManager)
            throw new NotSupportedException("This mod lifecycle cannot prepare a pinned schema reload.");
        return PrepareCandidateAsync(target: null, cancellationToken, schemaReload: true);
#else
        throw new NotSupportedException("Coordinated Lua schema reload is only available in Debug.");
#endif
    }

    private async ValueTask<ISimulationCodeReloadCandidate> PrepareCandidateAsync(ModStartupDependencies? target,
        CancellationToken cancellationToken, bool schemaReload = false, Action<IReadOnlyList<ModDefinition>>? checkCapture = null)
    {
        RequireOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        Revision basis = _current ?? throw new InvalidOperationException("Lua startup has not completed.");
        if (_preparing || _pending is not null || _faulted)
            throw new InvalidOperationException("A Lua replacement is in flight or the Simulation must stop.");
        cancellationToken.ThrowIfCancellationRequested();
        _preparing = true;
        ModContainer[]? pending = null;
        Vm[] created = [];
        ComponentQueryPreparer.StagedCatalog? catalog = null;
        WireProjection? wire = null;
        try
        {
            var previous = basis.Vms.ToDictionary(static vm => vm.Container.ModDefinition.MetaData.Id, StringComparer.Ordinal);
            IReadOnlyList<ModDefinition> definitions = target is null ?
                basis.Containers.Select(static owner => owner.ModDefinition).ToArray() : target.ScriptedMods;
            var next = new Vm[definitions.Count];
            var toLoad = new List<ModDefinition>();
            var kept = new HashSet<Vm>();
            for (int i = 0; i < definitions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = definitions[i];
                if (target is not null && previous.TryGetValue(definition.MetaData.Id, out var old) && CanReuse(old.Container.ModDefinition, definition))
                {
                    next[i] = old;
                    kept.Add(old);
                }
                else toLoad.Add(definition);
            }
            pending = target is null && !schemaReload
                ? await _lifecycle.PrepareReload(toLoad, _side, mainThreadScheduler, _logger, cancellationToken)
                : await ((ModsLifecycleManager)_lifecycle).PreparePinned(toLoad, _side, mainThreadScheduler, _logger, cancellationToken,
                    recaptureSource: schemaReload, checkCapture);
            return await OnOwnerAsync<ISimulationCodeReloadCandidate>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                created = new Vm[pending.Length];
                int index = 0;
                for (int slot = 0; slot < next.Length; slot++)
                {
                    if (next[slot] is not null) continue;
                    ModContainer container = pending[index];
                    var id = container.ModDefinition.MetaData.Id;
                    bool replace = previous.TryGetValue(id, out var old);
                    if (old is not null)
                    {
                        container.IsEnabled = old.Container.IsEnabled;
                        var oldRuntime = old.Container.Runtime as LuaScriptRuntime;
                        var newRuntime = container.Runtime as LuaScriptRuntime;
                        if (target is not null)
                            ComponentSchemaRegistry.RequireSameForSurvivor(id, oldRuntime!.Components, newRuntime!.Components);
                        if (!schemaReload && !LuaComponentDeclarations.SameWire(oldRuntime?.WireComponents, newRuntime?.WireComponents))
                            throw new InvalidDataException($"Live selection or standalone reload cannot change the wire projection of surviving mod '{id}'.");
                    }
                    next[slot] = created[index++] = new Vm(container, replace);
                }
                Vm[] retiring = basis.Vms.Where(vm => !kept.Contains(vm)).ToArray();
                ComponentQueryPreparer[] retiringBindings = retiring.Where(static vm => vm.Bindings is not null)
                    .Select(static vm => vm.Bindings!).ToArray();
                if (basis.Wire is { } oldWire) retiringBindings = [..retiringBindings, oldWire.Domain];
                catalog = _queryPreparer.PrepareCatalog(ReadSchemas(next), retiringBindings,
                    allowPoolChanges: target is not null || schemaReload, migrations: schemaReload ? ReadMigrations(next) : null);
                foreach (Vm vm in created)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Bind(vm, catalog);
                }
                var composition = target ?? basis.Composition?.WithScriptDefinitions(pending.Select(static owner => owner.ModDefinition).ToArray());
                wire = PrepareWire(next, catalog);
                return _pending = new Candidate(this, new Revision(next, composition, wire), catalog, created, retiring, basis.Wire,
                    legacy: target is null && !schemaReload, proveSchema: target is not null || checkCapture is not null);
            });
        }
        catch (Exception preparationError)
        {
            if (preparationError is SimulationCodeReloadCleanupException) _faulted = true;
            if (pending is not null)
            {
                try
                {
                    await OnOwnerAsync(() =>
                    {
                        Cleanup(created, pending, catalog, retireRoot: false, wire);
                        return true;
                    });
                }
                catch (Exception cleanupError)
                {
                    _faulted = true;
                    throw new SimulationCodeReloadCleanupException("Failed to clean a rejected Lua code replacement.",
                        new AggregateException(preparationError, cleanupError));
                }
            }
            throw;
        }
        finally { _preparing = false; }
    }

    private static bool CanReuse(ModDefinition accepted, ModDefinition target)
    {
        if (accepted.ScriptSource is null || !ReferenceEquals(accepted.ScriptSource, target.ScriptSource) ||
            !accepted.MetaData.Equals(target.MetaData) || !string.Equals(accepted.Directory, target.Directory, StringComparison.Ordinal))
            throw new InvalidDataException($"Live selection cannot change accepted Lua source or metadata of '{accepted.MetaData.Id}'.");
        var a = accepted.ResolvedDirectDependencies;
        var b = target.ResolvedDirectDependencies;
        return a is null || a.Count == 0 ? b is null || b.Count == 0 : b is not null && a.SetEquals(b);
    }

    private ComponentSchema[] ReadSchemas(Vm[] vms)
    {
        var schemas = new List<ComponentSchema>();
        foreach (Vm vm in vms)
            if (vm.Container.Runtime is LuaScriptRuntime runtime) schemas.AddRange(runtime.Components);
        foreach (ComponentSchema schema in schemas)
            if (components.Bindings.TryResolveType(schema.Id, out _))
                throw new InvalidDataException($"Dynamic schema '{schema.Id}' conflicts with a CLR component registration.");
        return schemas.ToArray();
    }

    private static Dictionary<string, ComponentMigration> ReadMigrations(Vm[] vms)
    {
        var result = new Dictionary<string, ComponentMigration>(StringComparer.Ordinal);
        foreach (Vm vm in vms)
            if (vm.Container.Runtime is LuaScriptRuntime { Migrations: { } declarations })
                foreach (var (id, migration) in declarations)
                    if (!result.TryAdd(id, migration)) throw new InvalidDataException($"Duplicate migration for '{id}'.");
        return result;
    }

    private WireProjection? PrepareWire(Vm[] vms, ComponentQueryPreparer.StagedCatalog? catalog)
    {
        var declarations = vms.SelectMany(static vm => vm.Container.Runtime is LuaScriptRuntime runtime ? runtime.WireComponents : [])
            .OrderBy(static declaration => declaration.Id, StringComparer.Ordinal).ToArray();
        if (declarations.Length == 0) return null;
        var domain = catalog is null ? _queryPreparer.CreateRevision() : catalog.CreateSequentialRevision();
        try { return new WireProjection(domain, declarations); }
        catch { domain.Dispose(); throw; }
    }

    private void Bind(Vm vm, ComponentQueryPreparer.StagedCatalog? catalog)
    {
        if (vm.Container.Runtime is not LuaScriptRuntime runtime || runtime.Systems.Length == 0) return;
        var bindings = vm.Bindings = catalog is null ? _queryPreparer.CreateSequentialRevision() : catalog.CreateSequentialRevision();
        runtime.PrepareEcs(bindings, bindings.CallbackScope, options, _logger);
        vm.Systems = new LuaUpdateSystem[runtime.Systems.Length];
        vm.Descriptors = new EcsUpdateSystemDescriptor[runtime.Systems.Length];
        for (int i = 0; i < runtime.Systems.Length; i++)
        {
            LuaSystemDeclaration declaration = runtime.Systems[i];
            vm.Systems[i] = new LuaUpdateSystem(vm.Container, runtime, i, time);
            vm.Descriptors[i] = new EcsUpdateSystemDescriptor(typeof(LuaUpdateSystem), true,
                runtime.GetContext(i).Accesses, declaration.Orders, declaration.Id);
        }
    }

    private void RequireOwner()
    {
        if (!(mainThreadScheduler?.IsMainThread ?? Environment.CurrentManagedThreadId == _ownerThread))
            throw new InvalidOperationException("Lua replacement requires its Simulation owner thread.");
    }

    private async ValueTask<T> OnOwnerAsync<T>(Func<T> operation)
    {
        if (mainThreadScheduler is not null) return await mainThreadScheduler.InvokeAsync(operation);
        RequireOwner();
        return operation();
    }

    private void Cleanup(Vm[] vms, ModContainer[] owners, ComponentQueryPreparer.StagedCatalog? catalog, bool retireRoot, WireProjection? wire)
    {
        List<Exception>? errors = null;
        try { wire?.Domain.RetireRevision(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        if (retireRoot)
        {
            try { _queryPreparer.RetireRevision(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        foreach (Vm? vm in vms)
        {
            if (vm is null) continue;
            try { vm.Bindings?.RetireRevision(); }
            catch (Exception error) { (errors ??= []).Add(error); }
            foreach (var adapter in vm.Systems) adapter.Retire();
        }
        // If a listener could not detach, keep pool storage owned until fail-stop teardown.
        if (errors is null)
        {
            try { catalog?.Dispose(); }
            catch (Exception error) { errors = [error]; }
        }
        try { LuaReloadCleanup.DisposeContainers(owners, _logger); }
        catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null)
            throw new SimulationCodeReloadCleanupException("Lua revision host cleanup failed.", new AggregateException(errors));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var current = _current;
        _current = null;
        try { _pending?.Finish(startNew: false); }
        finally { if (current is not null) Cleanup(current.Vms, current.Containers, null, retireRoot: true, current.Wire); }
    }

    private sealed class Candidate(LuaSimulationPreparation owner, Revision revision,
        ComponentQueryPreparer.StagedCatalog catalog, Vm[] created, Vm[] retiring, WireProjection? previousWire, bool legacy, bool proveSchema) : ISimulationCodeReloadCandidate
#if DEBUG
        , IPreparedSchemaReload
#endif
    {
        public ReadOnlyMemory<byte> SharedSchemaProof { get; private set; }
        public int? MaximumModReplicaRowBytes => _ready ? revision.Wire?.MaxRowBytes ?? 0 : null;
        private ComponentQueryPreparer.StagedCatalog? _catalog = catalog;
        private Vm[] _created = created;
        private Vm[] _retiring = retiring;
        private ModContainer[] _retiringOwners = Array.ConvertAll(retiring, static vm => vm.Container);
        private ModContainer[] _createdOwners = Array.ConvertAll(created, static vm => vm.Container);
        private bool _activationAttempted, _ready, _active, _finished;

        internal WireProjection? Wire => revision.Wire;
        public ReadOnlyMemory<ISystemUpdate> Systems => revision.Systems;
        public ReadOnlyMemory<EcsUpdateSystemDescriptor> Descriptors => revision.Descriptors;

        public void PrepareActivation(CancellationToken cancellationToken)
        {
            owner.RequireOwner();
            ObjectDisposedException.ThrowIf(_finished, this);
            if (_activationAttempted) throw new InvalidOperationException("Replacement activation was already prepared.");
            _activationAttempted = true;
            foreach (Vm vm in _created)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (vm.Replacement) vm.Container.Runtime.Reload();
            }
            cancellationToken.ThrowIfCancellationRequested();
            try { _catalog!.PreparePublication(cancellationToken); }
            catch (SimulationCodeReloadCleanupException) { owner._faulted = true; throw; }
            if (proveSchema) SharedSchemaProof = ProveSharedSchema(revision);
            _ready = true;
        }

        public void Activate()
        {
            owner.RequireOwner();
            ObjectDisposedException.ThrowIf(_finished || owner._disposed, this);
            if (!_ready || _active) throw new InvalidOperationException("Replacement activation is not ready.");
            try
            {
                _catalog!.RequireReady();
                previousWire?.Domain.RevokeRevision();
                if (legacy) owner._queryPreparer.RevokeRevision();
                foreach (Vm vm in _retiring) vm.Bindings?.RevokeRevision();
                foreach (Vm vm in _created)
                {
                    vm.Bindings?.ActivateRevision();
                    if (vm.Replacement) vm.Container.ActivateReload();
                }
                owner._lifecycle.ReplaceForReload(revision.Containers);
                _catalog!.Publish();
                revision.Wire?.Domain.ActivateRevision();
                owner._current = revision;
                _active = true;
            }
            catch { owner._faulted = true; throw; }
        }

        public void RetirePrevious()
        {
            owner.RequireOwner();
            if (!_active) throw new InvalidOperationException("A replacement must be active before retiring its predecessor.");
            Finish(startNew: true);
        }

        internal void Finish(bool startNew)
        {
            if (_finished) return;
            _finished = true;
            try
            {
                owner.Cleanup(_active ? _retiring : _created, _active ? _retiringOwners : _createdOwners, _catalog,
                    retireRoot: _active && legacy, _active ? previousWire : revision.Wire);
                if (_active && startNew)
                    foreach (Vm vm in _created)
                        if (!vm.Replacement) vm.Container.Start();
            }
            catch { owner._faulted = true; throw; }
            finally
            {
                _catalog = null;
                _created = _retiring = [];
                _createdOwners = _retiringOwners = [];
                owner._pending = null;
            }
        }

        public void Dispose()
        {
            if (_active || _finished) return;
            owner.RequireOwner();
            Finish(startNew: false);
        }
    }

    private sealed class WireProjection : IRevisionReplicaProjection
    {
        private const string ClaimOwner = "mod-wire";
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private readonly (PreparedComponentHandle Handle, ComponentFieldDescriptor[] Fields)[] _components;
        internal ComponentQueryPreparer Domain { get; }
        internal int MaxRowBytes { get; }
        int? IRevisionReplicaProjection.MaximumRowBytes => MaxRowBytes;
        public long SchemaHash { get; }
        internal byte[] Digest { get; }

        internal WireProjection(ComponentQueryPreparer domain, LuaWireComponent[] declarations)
        {
            Domain = domain;
            _components = new (PreparedComponentHandle, ComponentFieldDescriptor[])[declarations.Length];
            using var recipe = new MemoryStream();
            using var hashInput = new BinaryWriter(recipe);
            hashInput.Write("Karpik.LuaWire.v1");
            hashInput.Write(declarations.Length);
            int width = 0;
            for (int i = 0; i < declarations.Length; i++)
            {
                var declaration = declarations[i];
                if (i > 0 && declarations[i - 1].Id == declaration.Id)
                    throw new InvalidDataException($"Duplicate wire projection '{declaration.Id}'.");
                var handle = domain.BorrowWireComponent(declaration.Id, ClaimOwner);
                var selected = new ComponentFieldDescriptor[declaration.Fields.Length];
                hashInput.Write(declaration.Id);
                hashInput.Write(declaration.Version);
                hashInput.Write(selected.Length);
                width++; // Component presence owns the whole projected component, not any other component.
                for (int j = 0; j < selected.Length; j++)
                {
                    var field = declaration.Fields[j];
                    selected[j] = BindField(handle.Fields.Span, field, declaration.Id);
                    hashInput.Write(field.Name);
                    hashInput.Write((byte)field.Kind);
                    width += LuaComponentDeclarations.WireSize(field.Kind);
                }
                if (width > RevisionReplicaBaseline.MaxBytes)
                    throw new InvalidDataException("The mod wire row exceeds the baseline byte budget.");
                _components[i] = (handle, selected);
            }
            MaxRowBytes = width;
            hashInput.Flush();
            Span<byte> digest = stackalloc byte[32];
            SHA256.HashData(recipe.GetBuffer().AsSpan(0, (int)recipe.Length), digest);
            Digest = digest.ToArray();
            SchemaHash = BinaryPrimitives.ReadInt64LittleEndian(digest);
            if (SchemaHash == 0) throw new InvalidDataException("Wire schema hash zero is reserved for no projection.");
        }

        private static ComponentFieldDescriptor BindField(ReadOnlySpan<ComponentFieldDescriptor> fields, LuaWireField expected, string id)
        {
            foreach (var field in fields)
                if (field.Path.Length == 1 && field.Path.Span[0].Name == expected.Name && field.Kind == expected.Kind && field.CanWrite)
                    return field;
            throw new InvalidDataException($"Prepared wire component '{id}' has no writable '{expected.Name}' field of the declared type.");
        }

        private void Enter(EcsWorld world)
        {
            if (Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException("Wire projection requires its Simulation owner thread.");
            Domain.RequireWireWorld(world);
            Domain.CallbackScope.Enter(ClaimOwner);
        }

        public void Write(EcsWorld world, int entity, IWriter writer)
        {
            Enter(world);
            try
            {
                foreach (var (handle, fields) in _components)
                {
                    bool present = handle.Has(entity);
                    writer.Put(present);
                    if (!present) continue;
                    foreach (var field in fields)
                        switch (field.Kind)
                        {
                            case SchemaScalarKind.Bool: writer.Put(field.Bool.Read(entity)); break;
                            case SchemaScalarKind.U8: writer.Put(field.U8.Read(entity)); break;
                            case SchemaScalarKind.U16: writer.Put(field.U16.Read(entity)); break;
                            case SchemaScalarKind.I32: writer.Put(field.I32.Read(entity)); break;
                            case SchemaScalarKind.I64: writer.Put(field.I64.Read(entity)); break;
                            case SchemaScalarKind.F32: writer.Put(field.F32.Read(entity)); break;
                            case SchemaScalarKind.F64: writer.Put(field.F64.Read(entity)); break;
                            default: throw new InvalidOperationException("Unprepared wire kind.");
                        }
                }
            }
            finally { Domain.CallbackScope.Exit(); }
        }

        public void Validate(IReader reader) => ReadRow(reader, entity: -1);

        public void Apply(EcsWorld world, int entity, IReader reader)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(entity);
            Enter(world);
            try { ReadRow(reader, entity); }
            finally { Domain.CallbackScope.Exit(); }
        }

        private void ReadRow(IReader reader, int entity)
        {
            bool apply = entity >= 0;
            foreach (var (handle, fields) in _components)
            {
                bool present = reader.GetBool();
                if (apply)
                {
                    bool exists = handle.Has(entity);
                    if (present && !exists) handle.Add(entity);
                    else if (!present && exists) handle.Del(entity);
                }
                if (!present) continue;
                foreach (var field in fields)
                    switch (field.Kind)
                    {
                        case SchemaScalarKind.Bool: Set(field.Bool, reader.GetBool()); break;
                        case SchemaScalarKind.U8: Set(field.U8, reader.GetByte()); break;
                        case SchemaScalarKind.U16: Set(field.U16, reader.GetUShort()); break;
                        case SchemaScalarKind.I32: Set(field.I32, reader.GetInt()); break;
                        case SchemaScalarKind.I64: Set(field.I64, reader.GetLong()); break;
                        case SchemaScalarKind.F32: Set(field.F32, reader.GetFloat()); break;
                        case SchemaScalarKind.F64: Set(field.F64, reader.GetDouble()); break;
                        default: throw new InvalidOperationException("Unprepared wire kind.");
                    }
            }
            void Set<T>(IComponentField<T> accessor, T value) where T : unmanaged
            {
                if (apply) accessor.Write(entity, value);
            }
        }
    }

    private sealed class Vm(ModContainer container, bool replacement = false)
    {
        internal readonly ModContainer Container = container;
        internal readonly bool Replacement = replacement;
        internal ComponentQueryPreparer? Bindings;
        internal LuaUpdateSystem[] Systems = [];
        internal EcsUpdateSystemDescriptor[] Descriptors = [];
    }

    private sealed class Revision(Vm[] vms, ModStartupDependencies? composition, WireProjection? wire)
    {
        internal readonly ModStartupDependencies? Composition = composition;
        internal readonly WireProjection? Wire = wire;
        internal readonly Vm[] Vms = vms;
        internal readonly ModContainer[] Containers = Array.ConvertAll(vms, static vm => vm.Container);
        internal readonly ISystemUpdate[] Systems = vms.SelectMany(static vm => vm.Systems).ToArray();
        internal readonly EcsUpdateSystemDescriptor[] Descriptors = vms.SelectMany(static vm => vm.Descriptors).ToArray();
    }
}
