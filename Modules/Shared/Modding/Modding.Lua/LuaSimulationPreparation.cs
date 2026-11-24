using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Modding.Lua.Systems;
using Karpik.Engine.Shared.Modding.Bindings;
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
    MainThreadScheduler? mainThreadScheduler = null) : ISimulationPreparation, ISimulationCodeReloadParticipant, IDisposable
{
    private readonly IModsLifecycleManager _lifecycle = lifecycle;
    private readonly ILogger<IScriptLoader> _logger = logger;
    private ModDefinition[] _definitions = [];
    private ExecutionSide _side;
    private Revision? _current;
    private int _ownerThread;
    private bool _preparing;
    private bool _disposed;

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
        _definitions = registry is ModsRegistry { StartupDependencies: { } startup }
            ? startup.ScriptedMods.ToArray()
            : registry.GetMods().OrderBy(definition => definition.MetaData.Id, StringComparer.Ordinal).ToArray();
        await _lifecycle.Load(_definitions, _side, cancellationToken);
        await OnOwnerAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ModContainer[] owners = _lifecycle.Containers.ToArray();
            ValidateSchemas(owners);
            queryPreparer.Initialize();
            components.Seal();
            Revision revision = Bind(owners, queryPreparer, callbackScope, cancellationToken);
            _current = revision;
            for (int index = 0; index < revision.Systems.Length; index++)
                systems.AddInstance(revision.Systems[index], revision.Descriptors[index]);
            return true;
        });
    }

    public async ValueTask<ISimulationCodeReloadCandidate> PrepareReloadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_current is null) throw new InvalidOperationException("Lua startup has not completed.");
        if (_preparing) throw new InvalidOperationException("A Lua code replacement is already prepared or preparing.");
        cancellationToken.ThrowIfCancellationRequested();
        _preparing = true;
        ModContainer[]? pending = null;
        ComponentQueryPreparer? bindingRevision = null;
        try
        {
            pending = await _lifecycle.PrepareReload(_definitions, _side, mainThreadScheduler, _logger, cancellationToken);
            return await OnOwnerAsync<ISimulationCodeReloadCandidate>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateSchemas(pending);
                bindingRevision = queryPreparer.CreateRevision();
                Revision revision = Bind(pending, bindingRevision, bindingRevision.CallbackScope, cancellationToken);
                var previousFlags = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (ModContainer owner in _lifecycle.Containers) previousFlags.Add(owner.ModDefinition.MetaData.Id, owner.IsEnabled);
                foreach (ModContainer owner in pending) owner.IsEnabled = previousFlags[owner.ModDefinition.MetaData.Id];
                return new Candidate(this, revision);
            });
        }
        catch (Exception preparationError)
        {
            _preparing = false;
            if (pending is not null)
            {
                try
                {
                    await OnOwnerAsync(() =>
                    {
                        CleanupUnpublished(pending, bindingRevision);
                        return true;
                    });
                }
                catch (Exception cleanupError)
                {
                    throw new SimulationCodeReloadCleanupException("Failed to clean a rejected Lua code replacement.",
                        new AggregateException(preparationError, cleanupError));
                }
            }
            throw;
        }
    }

    private void ValidateSchemas(ModContainer[] owners)
    {
        var schemas = new List<ComponentSchema>();
        foreach (ModContainer owner in owners)
            if (owner.Runtime is LuaScriptRuntime runtime) schemas.AddRange(runtime.Components);
        foreach (ComponentSchema schema in schemas)
            if (components.Bindings.TryResolveType(schema.Id, out _))
                throw new InvalidDataException($"Dynamic schema '{schema.Id}' conflicts with a CLR component registration.");
        schemaRegistry.ValidateAndFreeze(schemas.ToArray());
    }

    private Revision Bind(ModContainer[] owners, ComponentQueryPreparer bindings, ComponentCallbackScope scope,
        CancellationToken cancellationToken)
    {
        var adapters = new List<ISystemUpdate>();
        var descriptors = new List<EcsUpdateSystemDescriptor>();
        foreach (ModContainer owner in owners)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.Runtime is not LuaScriptRuntime runtime) continue;
            runtime.PrepareEcs(bindings, scope, options, _logger);
            for (int index = 0; index < runtime.Systems.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LuaSystemDeclaration declaration = runtime.Systems[index];
                adapters.Add(new LuaUpdateSystem(owner, runtime, index, time));
                descriptors.Add(new EcsUpdateSystemDescriptor(typeof(LuaUpdateSystem), true,
                    runtime.GetContext(index).Accesses, declaration.Orders, declaration.Id));
            }
        }
        return new Revision(owners, bindings, adapters.ToArray(), descriptors.ToArray());
    }

    private async ValueTask<T> OnOwnerAsync<T>(Func<T> operation)
    {
        if (mainThreadScheduler is not null) return await mainThreadScheduler.InvokeAsync(operation);
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Asynchronous Lua reload preparation requires a main-thread scheduler.");
        return operation();
    }

    private void CleanupUnpublished(ModContainer[] owners, ComponentQueryPreparer? bindings)
    {
        Exception? bindingError = null;
        try { bindings?.Dispose(); }
        catch (Exception error) { bindingError = error; }
        try { LuaReloadCleanup.DisposeContainers(owners, _logger); }
        catch (Exception error)
        {
            throw new SimulationCodeReloadCleanupException("Rejected Lua revision cleanup failed.",
                bindingError is null ? error : new AggregateException(bindingError, error));
        }
        if (bindingError is not null)
            throw new SimulationCodeReloadCleanupException("Rejected Lua binding cleanup failed.", bindingError);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Revision? revision = _current;
        _current = null;
        revision?.Retire(_logger);
    }

    private sealed class Candidate(LuaSimulationPreparation owner, Revision revision) : ISimulationCodeReloadCandidate
    {
        private Revision? _previous;
        private bool _activationAttempted;
        private bool _ready;
        private bool _active;
        private bool _disposed;

        public ReadOnlyMemory<ISystemUpdate> Systems => revision.Systems;
        public ReadOnlyMemory<EcsUpdateSystemDescriptor> Descriptors => revision.Descriptors;

        public void PrepareActivation(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activationAttempted) throw new InvalidOperationException("Replacement activation was already prepared.");
            _activationAttempted = true;
            foreach (ModContainer container in revision.Containers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                container.Runtime.Reload();
            }
            cancellationToken.ThrowIfCancellationRequested();
            _ready = true;
        }

        public void Activate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ObjectDisposedException.ThrowIf(owner._disposed, owner);
            if (!_ready || _active) throw new InvalidOperationException("Replacement activation is not ready.");
            revision.Bindings.ActivateRevision();
            foreach (ModContainer container in revision.Containers) container.ActivateReload();
            _previous = owner._current;
            owner._lifecycle.ReplaceForReload(revision.Containers);
            owner._current = revision;
            owner._preparing = false;
            _active = true;
        }

        public void RetirePrevious()
        {
            if (!_active) throw new InvalidOperationException("A replacement must be active before retiring its predecessor.");
            Revision? previous = _previous;
            _previous = null;
            previous?.Retire(owner._logger);
        }

        public void Dispose()
        {
            if (_disposed || _active) return;
            _disposed = true;
            owner._preparing = false;
            revision.Retire(owner._logger);
        }
    }

    private sealed class Revision(ModContainer[] containers, ComponentQueryPreparer bindings,
        ISystemUpdate[] systems, EcsUpdateSystemDescriptor[] descriptors)
    {
        internal ModContainer[] Containers { get; } = containers;
        internal ComponentQueryPreparer Bindings { get; } = bindings;
        internal ISystemUpdate[] Systems { get; } = systems;
        internal EcsUpdateSystemDescriptor[] Descriptors { get; } = descriptors;
        private bool _retired;

        internal void Retire(ILogger logger)
        {
            if (_retired) return;
            _retired = true;
            List<Exception>? failures = null;
            try { Bindings.RetireRevision(); }
            catch (Exception error) { (failures ??= []).Add(error); }
            foreach (ISystemUpdate system in Systems)
                if (system is LuaUpdateSystem adapter) adapter.Retire();
            try { LuaReloadCleanup.DisposeContainers(Containers, logger); }
            catch (Exception error) { (failures ??= []).Add(error); }
            if (failures is not null)
                throw new SimulationCodeReloadCleanupException("Lua revision host cleanup failed.", new AggregateException(failures));
        }
    }
}
