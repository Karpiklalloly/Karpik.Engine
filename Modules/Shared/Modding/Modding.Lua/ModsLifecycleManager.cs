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
    
    public async JobHandle<ModContainer[]> PrepareReload(IReadOnlyList<ModDefinition> definitions,
        ExecutionSide side, MainThreadScheduler? scheduler, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_loaded || _loading) throw new InvalidOperationException("Mods must be loaded before code reload.");
        if (_reloadIntegrityLost) throw new SimulationCodeReloadCleanupException(
            "A previous unsupported asynchronous load requires owner-thread teardown.", new InvalidOperationException());
        int ownerThread = Environment.CurrentManagedThreadId;
        var pending = new List<ModContainer>(definitions.Count);
        try
        {
            foreach (ModDefinition definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JobHandle<IScriptRuntime> loading = scheduler is null
                    ? scriptLoader.Load(definition, side)
                    : await scheduler.InvokeAsync(() => scriptLoader.Load(definition, side));
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
