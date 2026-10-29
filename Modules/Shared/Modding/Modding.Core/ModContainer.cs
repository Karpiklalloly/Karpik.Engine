namespace Karpik.Engine.Shared.Modding;

public class ModContainer : IDisposable
{
    public ModDefinition ModDefinition { get; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>The runtime owned and disposed by this container.</summary>
    public IScriptRuntime Runtime => _runtime;

    private readonly IScriptRuntime _runtime;
    private bool _started = false;
    private bool _loaded = false;
    private bool _disposed = false;
    
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
        if (!TryStartUpdate()) return;
        _runtime.Update(dt);
    }
    
    public void FixedUpdate(double fixedDt)
    {
        if (!TryStartUpdate()) return;
        _runtime.FixedUpdate(fixedDt);
    }

    /// <summary>Starts an enabled mod once and checks whether its update may run; throws after disposal.</summary>
    public bool TryStartUpdate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsEnabled) return false;
        Start();
        return IsEnabled;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_started)
                _runtime.Destroy();
        }
        finally
        {
            try
            {
                if (_loaded)
                    _runtime.Unload();
            }
            finally
            {
                _runtime.Dispose();
            }
        }
    }
}
