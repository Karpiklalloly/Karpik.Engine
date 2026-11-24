using System.Runtime.ExceptionServices;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Modding;

public class ModContainer : IDisposable
{
    public ModDefinition ModDefinition { get; }
    public bool IsEnabled { get; set; } = true;
    /// <summary>The runtime owned and disposed by this container.</summary>
    public IScriptRuntime Runtime => _runtime;

    private readonly IScriptRuntime _runtime;
    private bool _started;
    private bool _loaded;
    private bool _unloadRequired;
    private bool _disposed;

    public ModContainer(ModDefinition modDefinition, IScriptRuntime runtime)
    {
        ModDefinition = modDefinition;
        _runtime = runtime;
    }

    public void Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loaded) return;
        _runtime.Load();
        _loaded = true;
        _unloadRequired = true;
    }

    /// <summary>A rejected replacement must unload even if its load callback failed partway.</summary>
    public void LoadForReload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _unloadRequired = true;
        Load();
    }

    /// <summary>Activates an initialized replacement without invoking initial gameplay spawn.</summary>
    public void ActivateReload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_loaded) throw new InvalidOperationException("A replacement must be loaded before activation.");
        _started = true;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsEnabled || _started) return;
        if (!_loaded)
            throw new InvalidOperationException($"Mod {ModDefinition.MetaData.Name} must be loaded first.");
        _runtime.Start();
        _started = true;
    }

    public void Update(double dt)
    {
        if (TryStartUpdate()) _runtime.Update(dt);
    }

    public void FixedUpdate(double fixedDt)
    {
        if (TryStartUpdate()) _runtime.FixedUpdate(fixedDt);
    }

    /// <summary>Starts an enabled mod once and checks whether its update may run; throws after disposal.</summary>
    public bool TryStartUpdate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsEnabled) return false;
        Start();
        return IsEnabled;
    }

    public void Dispose() => DisposeCore(reloading: false);

    /// <summary>Preserves host-disposal phase information for transactional reload cleanup.</summary>
    public void DisposeForCodeReload() => DisposeCore(reloading: true);

    private void DisposeCore(bool reloading)
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? errors = null;
        try { if (_started) _runtime.Destroy(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        try { if (_unloadRequired) _runtime.Unload(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        try { _runtime.Dispose(); }
        catch (Exception error)
        {
            (errors ??= []).Add(reloading
                ? new SimulationCodeReloadCleanupException("Mod runtime host disposal failed.", error)
                : error);
        }
        if (errors?.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors is not null) throw new AggregateException("Mod cleanup failed.", errors);
    }
}
