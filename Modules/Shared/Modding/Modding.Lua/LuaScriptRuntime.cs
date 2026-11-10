using MoonSharp.Interpreter;

using Karpik.Engine.Shared.Modding.Bindings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    internal LuaSystemDeclaration[] Systems { get; }
    internal bool HasDeclaredSystems { get; }
    internal ComponentSchema[] Components { get; }
    private LuaEcsContext[]? _contexts;
    private ScriptSystemOptions _systemOptions = new();
    private ILogger _systemLogger = NullLogger.Instance;
    private readonly DynValue[] _contextArguments = new DynValue[2];
    private readonly DynValue[] _systemCallbacks;
    private bool _disposed = false;
    
    private readonly DynValue[] _dtArguments = new DynValue[1];
    private bool _invoking;
    
    internal LuaScriptRuntime(Script script, Table[] entryPoints, string modId, string[] paths)
    {
        _script = script;
        try
        {
            Systems = LuaSystemDeclarations.Read(modId, entryPoints, out bool declared);
            HasDeclaredSystems = declared;
            Components = LuaComponentDeclarations.Read(modId, entryPoints);
        }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException($"Mod '{modId}' ({string.Join(", ", paths)}): {error.Message}", error);
        }
        _systemCallbacks = Array.ConvertAll(Systems, system => system.Callback);
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
        Array.Clear(_systemCallbacks);
        Array.Clear(Systems);
        Array.Clear(Components);
        if (_contexts is not null) Array.Clear(_contexts);
        _contexts = null;
    }
    
    private void Invoke(Callback callback, double? dt = null)
        => Invoke(_callbacks[(int)callback], dt);

    internal void PrepareEcs(ComponentQueryPreparer preparer, ComponentCallbackScope scope, ScriptSystemOptions options, ILogger logger)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_contexts is not null) throw new InvalidOperationException("Lua ECS contexts are already prepared.");
        if (options.ErrorPolicy is not (ScriptSystemErrorPolicy.StopSimulation or ScriptSystemErrorPolicy.LogAndContinue))
            throw new ArgumentOutOfRangeException(nameof(options));
        _systemOptions = options; _systemLogger = logger;
        _contexts = Array.ConvertAll(Systems, declaration => new LuaEcsContext(_script, declaration.Id, declaration.Queries, preparer, scope));
    }

    internal LuaEcsContext GetContext(int index) => _contexts?[index] ?? throw new InvalidOperationException("Lua ECS contexts are not prepared.");

    internal void InvokeSystem(int index, double dt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_invoking) throw new InvalidOperationException("Recursive runtime execution is not supported.");
        LuaEcsContext? context = _contexts?[index];
        if (context is null)
        {
            if (Systems[index].Queries.Length != 0) throw new InvalidOperationException("Lua ECS contexts are not prepared.");
            Invoke(_systemCallbacks.AsSpan(index, 1), dt); return;
        }
        context.Enter();
        try { Invoke(_systemCallbacks.AsSpan(index, 1), dt, context.Value); }
        catch (Exception error) when (_systemOptions.ErrorPolicy == ScriptSystemErrorPolicy.LogAndContinue && error is not (OperationCanceledException or OutOfMemoryException))
        {
            _systemLogger.LogError(error, "Lua system {SystemId}, queries {Queries} callback failed; partial writes retained", Systems[index].Id, context.QueryNames);
        }
        finally { context.Exit(); }
    }

    private void Invoke(ReadOnlySpan<DynValue> functions, double? dt, DynValue? context = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_invoking)
            throw new InvalidOperationException("Recursive runtime execution is not supported.");

        if (dt.HasValue && (!double.IsFinite(dt.Value) || dt.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(dt));

        if (functions.Length == 0) return;

        _invoking = true;

        try
        {
            DynValue[] arguments = Array.Empty<DynValue>();

            if (dt.HasValue)
            {
                // TODO: allocating MoonSharp calls; revisit before production ticks.
                arguments = context is null ? _dtArguments : _contextArguments;
                arguments[0] = DynValue.NewNumber(dt.Value);
                if (context is not null) arguments[1] = context;
            }

            foreach (DynValue function in functions)
            {
                _script.Call(function, arguments);
            }
        }
        finally
        {
            _dtArguments[0] = DynValue.Nil;
            _contextArguments[0] = _contextArguments[1] = DynValue.Nil;
            _invoking = false;
        }
    }
}
