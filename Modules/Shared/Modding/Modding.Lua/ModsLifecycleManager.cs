using System.Composition;
using Karpik.Engine.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IModsLifecycleManager))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class ModsLifecycleManager(IScriptLoader scriptLoader) : IModsLifecycleManager, IDisposable
{
    public IReadOnlyList<ModContainer> Containers => _containers;
    
    private ModContainer[] _containers = [];
    private bool _loading;
    private bool _loaded;
    private bool _disposed;
    private List<ModContainer>? _deferredReloadCleanup;
    private ILogger? _deferredReloadLogger;
    private int _deferredCleanupOwnerThread;
    private bool _reloadIntegrityLost;
    
    public async JobHandle Load(IReadOnlyList<ModDefinition> definitions, ExecutionSide side,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_loading || _loaded)
            throw new InvalidOperationException("Mods are already loading or loaded.");

        _loading = true;
        List<ModContainer> pending = new List<ModContainer>(definitions.Count);

        try
        {
            definitions = PinLuaDefinitions(definitions, side, fresh: false, cancellationToken);
            foreach (ModDefinition definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IScriptRuntime runtime = await scriptLoader.Load(definition, side);

                ModContainer container = new ModContainer(definition, runtime);
                pending.Add(container);

                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                container.Load();
            }

            cancellationToken.ThrowIfCancellationRequested();
            _containers = pending.ToArray();
            _loaded = true;
        }
        catch (Exception loadError)
        {
            try
            {
                DisposeAll(pending);
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(loadError, cleanupError);
            }

            throw;
        }
        finally
        {
            _loading = false;
        }
    }
    
    public JobHandle<ModContainer[]> PrepareReload(IReadOnlyList<ModDefinition> definitions,
        ExecutionSide side, MainThreadScheduler? scheduler, ILogger logger,
        CancellationToken cancellationToken = default) => PrepareInactive(definitions, side, scheduler, logger, fresh: true, cancellationToken);

    internal JobHandle<ModContainer[]> PreparePinned(IReadOnlyList<ModDefinition> definitions,
        ExecutionSide side, MainThreadScheduler? scheduler, ILogger logger, CancellationToken cancellationToken, bool recaptureSource = false,
        Action<IReadOnlyList<ModDefinition>>? checkCapture = null)
    {
        if (scriptLoader is not LuaScriptLoader lua || lua.GetType() != typeof(LuaScriptLoader) ||
            (recaptureSource && !LuaSourceCapture.IsSupported))
            return JobHandle<ModContainer[]>.FromException(new NotSupportedException("Live mods require the standard pinned Lua loader and verified capture."));
        foreach (var definition in definitions)
            if (definition.ScriptSource is null || definition.ScriptSource.Side != side)
                return JobHandle<ModContainer[]>.FromException(new InvalidDataException($"Mod '{definition.MetaData.Id}' has no accepted source for {side}."));
        return PrepareInactive(definitions, side, scheduler, logger, fresh: recaptureSource, cancellationToken, pinnedLoader: lua, checkCapture);
    }

    private async JobHandle<ModContainer[]> PrepareInactive(IReadOnlyList<ModDefinition> definitions,
        ExecutionSide side, MainThreadScheduler? scheduler, ILogger logger, bool fresh,
        CancellationToken cancellationToken, LuaScriptLoader? pinnedLoader = null,
        Action<IReadOnlyList<ModDefinition>>? checkCapture = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_loaded || _loading) throw new InvalidOperationException("Mods must be loaded before code reload.");
        if (_reloadIntegrityLost) throw new SimulationCodeReloadCleanupException(
            "A previous unsupported asynchronous load requires owner-thread teardown.", new InvalidOperationException());
        int ownerThread = Environment.CurrentManagedThreadId;
        var pending = new List<ModContainer>(definitions.Count);
        try
        {
            if (fresh && checkCapture is not null && scheduler is not null)
            {
                // Coordinated capture must not block the owner that also services revision control.
                var inputs = definitions;
                definitions = await Task.Run(() => PinLuaDefinitions(inputs, side, fresh: true, cancellationToken), cancellationToken);
                await scheduler.InvokeAsync(() => { cancellationToken.ThrowIfCancellationRequested(); checkCapture(definitions); });
            }
            else
            {
                if (fresh) definitions = PinLuaDefinitions(definitions, side, fresh: true, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                checkCapture?.Invoke(definitions);
            }
            foreach (ModDefinition definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JobHandle<IScriptRuntime> loading = scheduler is null
                    ? LoadOne(definition)
                    : await scheduler.InvokeAsync(() => LoadOne(definition));
                IScriptRuntime runtime = await loading;
                var container = new ModContainer(definition, runtime);
                pending.Add(container);
                if (scheduler is not null)
                {
                    await scheduler.InvokeAsync(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        container.LoadForReload();
                    });
                }
                else
                {
                    RequireOwnerThread(ownerThread);
                    cancellationToken.ThrowIfCancellationRequested();
                    container.LoadForReload();
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return pending.ToArray();
        }
        catch (Exception preparationError)
        {
            if (scheduler is null && Environment.CurrentManagedThreadId != ownerThread)
            {
                // An optional-scheduler caller used an asynchronous loader. Keep ownership until
                // the caller performs owner-thread teardown; never invoke VM cleanup off-owner.
                _reloadIntegrityLost = true;
                _deferredReloadCleanup = pending;
                _deferredReloadLogger = logger;
                _deferredCleanupOwnerThread = ownerThread;
                throw new SimulationCodeReloadCleanupException(
                    "Asynchronous Lua reload requires a main-thread scheduler; pending resources require owner-thread teardown.", preparationError);
            }
            try
            {
                if (scheduler is not null)
                    await scheduler.InvokeAsync(() => LuaReloadCleanup.DisposeContainers(pending, logger));
                else
                {
                    RequireOwnerThread(ownerThread);
                    LuaReloadCleanup.DisposeContainers(pending, logger);
                }
            }
            catch (Exception cleanupError)
            {
                throw new SimulationCodeReloadCleanupException("Failed to clean a partially loaded Lua replacement.",
                    new AggregateException(preparationError, cleanupError));
            }
            throw;
        }

        // The real Lua loader grants only the sandbox globals and accepted imports in live mode.
        JobHandle<IScriptRuntime> LoadOne(ModDefinition definition) => pinnedLoader is null
            ? scriptLoader.Load(definition, side) : pinnedLoader.Load(definition, side);
    }

    private IReadOnlyList<ModDefinition> PinLuaDefinitions(IReadOnlyList<ModDefinition> definitions, ExecutionSide side,
        bool fresh, CancellationToken cancellationToken)
    {
        if (scriptLoader is not LuaScriptLoader || !LuaSourceCapture.IsSupported) return definitions;
        var budget = new LuaSourceCapture.Budget();
        var pinned = new ModDefinition[definitions.Count];
        for (int i = 0; i < pinned.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pinned[i] = budget.Pin(definitions[i], side, cancellationToken, fresh);
        }
        return pinned;
    }

    public ModContainer[] ReplaceForReload(ModContainer[] containers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_loaded || _loading) throw new InvalidOperationException("Mods must be loaded before code reload.");
        ModContainer[] previous = _containers;
        _containers = containers;
        return previous;
    }

    private static void RequireOwnerThread(int ownerThread)
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("Asynchronous Lua reload preparation requires a main-thread scheduler.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_deferredReloadCleanup is not null) RequireOwnerThread(_deferredCleanupOwnerThread);
        _disposed = true;
        ModContainer[] containers = _containers;
        _containers = [];
        List<ModContainer>? deferred = _deferredReloadCleanup;
        _deferredReloadCleanup = null;
        List<Exception>? errors = null;
        try { DisposeAll(containers); } catch (Exception error) { (errors ??= []).Add(error); }
        if (deferred is not null)
        {
            try { LuaReloadCleanup.DisposeContainers(deferred, _deferredReloadLogger!); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        _deferredReloadLogger = null;
        if (errors is not null) throw new AggregateException("Mod manager cleanup failed.", errors);
    }
    
    private static void DisposeAll(IReadOnlyList<ModContainer> containers)
    {
        List<Exception>? errors = null;

        for (int i = containers.Count - 1; i >= 0; i--)
        {
            try
            {
                containers[i].Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }


        if (errors is not null)
            throw new AggregateException("Mod cleanup failed.", errors);
    }
}
