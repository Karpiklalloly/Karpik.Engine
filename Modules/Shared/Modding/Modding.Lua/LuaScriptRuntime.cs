using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

public sealed class LuaScriptRuntime : IScriptRuntime
{
    private enum Callback
    {
        Load,
        Start,
        Update,
        FixedUpdate,
        Destroy,
        Unload
    }
    
    private Script _script;
    private readonly DynValue[][] _callbacks;
    private bool _disposed = false;
    
    private readonly DynValue[] _dtArguments = new DynValue[1];
    private bool _invoking;
    
    internal LuaScriptRuntime(Script script, Table[] entryPoints)
    {
        _script = script;
        _callbacks =
        [
            Bind("on_load"),
            Bind("on_start"),
            Bind("on_update"),
            Bind("on_fixed_update"),
            Bind("on_destroy"),
            Bind("on_unload")
        ];
        
        DynValue[] Bind(string name)
        {
            var functions = new List<DynValue>(entryPoints.Length);

            foreach (Table entryPoint in entryPoints)
            {
                DynValue function = entryPoint.Get(name);

                if (function.IsNil())
                    continue;

                if (function.Type != DataType.Function)
                {
                    throw new InvalidDataException(
                        $"Callback '{name}' must be a Lua function.");
                }

                functions.Add(function);
            }

            return functions.ToArray();
        }
    }

    public void Load() => Invoke(Callback.Load);

    public void Start() => Invoke(Callback.Start);

    public void Update(double dt) => Invoke(Callback.Update, dt);

    public void FixedUpdate(double fixedDt) => Invoke(Callback.FixedUpdate, fixedDt);

    public void Destroy() => Invoke(Callback.Destroy);

    public void Unload() => Invoke(Callback.Unload);
    
    public void Dispose()
    {
        if (_disposed)
            return;
        
        if (_invoking)
            throw new InvalidOperationException("Cannot dispose during a callback.");

        _disposed = true;
        _script = null!;

        foreach (DynValue[] functions in _callbacks)
        {
            Array.Clear(functions, 0, functions.Length);
        }
    }
    
    private void Invoke(Callback callback, double? dt = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_invoking)
            throw new InvalidOperationException("Recursive runtime execution is not supported.");

        if (dt.HasValue && (!double.IsFinite(dt.Value) || dt.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(dt));

        DynValue[] functions = _callbacks[(int)callback];
        if (functions.Length == 0) return;

        _invoking = true;

        try
        {
            DynValue[] arguments = Array.Empty<DynValue>();

            if (dt.HasValue)
            {
                // TODO: allocating MoonSharp calls; revisit before production ticks.
                _dtArguments[0] = DynValue.NewNumber(dt.Value);
                arguments = _dtArguments;
            }

            foreach (DynValue function in functions)
            {
                _script.Call(function, arguments);
            }
        }
        finally
        {
            _dtArguments[0] = DynValue.Nil;
            _invoking = false;
        }
    }
}