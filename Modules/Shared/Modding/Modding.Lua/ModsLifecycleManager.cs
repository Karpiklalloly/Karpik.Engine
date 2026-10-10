using System.Composition;
using Karpik.Engine.Core;
using Karpik.Jobs;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IModsLifecycleManager))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public class ModsLifecycleManager(IScriptLoader scriptLoader) : IModsLifecycleManager, IDisposable
{
    public IReadOnlyList<ModContainer> Containers => _containers;
    
    private readonly List<ModContainer> _containers = [];
    private bool _loading;
    private bool _loaded;
    private bool _disposed;
    
    public async JobHandle Load(IReadOnlyList<ModDefinition> definitions, ExecutionSide side)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_loading || _loaded)
            throw new InvalidOperationException("Mods are already loading or loaded.");

        _loading = true;
        List<ModContainer> pending = new List<ModContainer>(definitions.Count);

        try
        {
            foreach (ModDefinition definition in definitions)
            {
                IScriptRuntime runtime = await scriptLoader.Load(definition, side);

                ModContainer container = new ModContainer(definition, runtime);
                pending.Add(container);

                ObjectDisposedException.ThrowIf(_disposed, this);
                container.Load();
            }

            _containers.AddRange(pending);
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
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeAll(_containers);
    }
    
    private static void DisposeAll(List<ModContainer> containers)
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

        containers.Clear();

        if (errors is not null)
            throw new AggregateException("Mod cleanup failed.", errors);
    }
}