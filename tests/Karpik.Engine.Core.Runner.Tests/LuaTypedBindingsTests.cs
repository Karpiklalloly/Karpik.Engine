using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Microsoft.Extensions.Logging.Abstractions;
using MoonSharp.Interpreter;
using Xunit;
using Xunit.Abstractions;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;

[Collection("Lua ECS allocation probes")]
public sealed partial class LuaTypedBindingsTests(ITestOutputHelper output)
{
    public struct Scalars : IEcsComponent
    { public bool B; public double D; public float F; public int I; public uint U; public readonly int ReadOnly; }
    public struct FloatScalars : IEcsComponent { public float B, D, F, I, U; }
    private const string MixedFields = "B='bool',D='f64',F='f32',I='i32',U='u32'";
    private const string Copy = "v.B=p.B; v.D=p.D; v.F=p.F; v.I=p.I; v.U=p.U";

    private static string Source(string fields, string body, string extraSystem = "") => $$$"""
        return {
          components = { Values = { version = 1, fields = { {{{fields}}} } } },
          systems = { {
            id='copy', phase='update', queries={q={
              inc={['game:Scalars']={readonly=false}}, exc={['weather:Values']={readonly=false}}
            }}, callback=function(dt,ctx)
              local q=ctx.queries.q
              local native=q.components['game:Scalars']
              local dynamic=q.components['weather:Values']
              for e in q:entities() do
                local p=native:get(e); local v=dynamic:add(e)
                {{{body}}}
              end
            end
          } {{{extraSystem}}} }
        }
        """;

    private sealed class Fixture : IDisposable
    {
        internal readonly Script Script = new();
        internal readonly EcsDefaultWorld World = new();
        internal readonly ComponentCallbackScope Scope = new();
        internal readonly LuaScriptRuntime Runtime;
        internal readonly ComponentQueryPreparer Preparer;
        private readonly bool _allFloat;
        internal Fixture(string body = Copy, string fields = MixedFields, string extraSystem = "",
            ScriptSystemErrorPolicy policy = ScriptSystemErrorPolicy.StopSimulation, bool allFloat = false)
        {
            _allFloat = allFloat;
            Runtime = new(Script, [Script.DoString(Source(fields, body, extraSystem)).Table], "weather", ["main.lua"]);
            var bindings = new ClrComponentBindings([new GeneratedBindings()]);
            if (allFloat) bindings.Register<FloatScalars>("game:Scalars"); else bindings.Register<Scalars>("game:Scalars");
            var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze(Runtime.Components);
            Preparer = new(World, bindings, schemas, Scope); Preparer.Initialize();
            Runtime.PrepareEcs(Preparer, Scope, new ScriptSystemOptions { ErrorPolicy = policy }, NullLogger.Instance);
        }
        internal PreparedComponentHandle Values => Runtime.GetContext(0).Queries[0].Components["weather:Values"];
        internal int Entity(bool uniform = false)
        {
            int entity = World.NewEntity();
            if (_allFloat) World.GetPool<FloatScalars>().Add(entity) = new() { B = 1, D = 1, F = 1, I = 1, U = 1 };
            else World.GetPool<Scalars>().Add(entity) = uniform
                ? new() { B = true, D = 1, F = 1, I = 1, U = 1 }
                : new() { B = true, D = 16777217d, F = 1.25f, I = -17, U = 17 };
            return entity;
        }
        internal void Inspect(Action action)
        { Scope.Enter("weather:copy"); try { action(); } finally { Scope.Exit(); } }
        internal void Reset(ReadOnlySpan<int> entities)
        {
            Scope.Enter("weather:copy");
            try { var values = Values; foreach (int entity in entities) if (values.Has(entity)) values.Del(entity); }
            finally { Scope.Exit(); }
        }
        internal double Read(int entity, string field)
        {
            var view = Values.Get(entity);
            return field switch { "D" => view.ReadF64(1), "F" => view.ReadF32(2), "I" => view.ReadI32(3), "U" => view.ReadU32(4), _ => throw new ArgumentException(field) };
        }
        public void Dispose() { Runtime.Dispose(); Preparer.Dispose(); Scope.Dispose(); World.Destroy(); }
    }

    [Fact]
    public void LuaEcsBindings_TypedScalarsRoundTripNativeAndDynamic()
    {
        const string back = """
            , {id='back',phase='update',queries={q={inc={['game:Scalars']={readonly=false},['weather:Values']={}}}},callback=function(dt,ctx)
              local q=ctx.queries.q
              for e in q:entities() do
                local p=q.components['game:Scalars']:get(e); local v=q.components['weather:Values']:get(e)
                assert(type(v.B)=='boolean' and type(v.I)=='number' and type(v.U)=='number' and type(v.D)=='number')
                p.B=not v.B; p.D=v.D+1; p.F=v.F+1; p.I=v.I+1; p.U=v.U+1
              end
            end}
            """;
        using var f = new Fixture(extraSystem: back); int entity = f.Entity();
        f.Runtime.InvokeSystem(0, 0.02); f.Runtime.InvokeSystem(1, 0.02);
        ref var native = ref f.World.GetPool<Scalars>().Get(entity);
        Assert.False(native.B); Assert.Equal(16777218d, native.D); Assert.Equal(2.25f, native.F);
        Assert.Equal(-16, native.I); Assert.Equal(18u, native.U);
        f.Inspect(() => { Assert.True(f.Values.Get(entity).ReadBool(0)); Assert.Equal(16777217d, f.Read(entity, "D")); });
    }

    [Theory]
    [InlineData("I", "-2147483648", -2147483648d)]
    [InlineData("I", "-1", -1d)]
    [InlineData("I", "0", 0d)]
    [InlineData("I", "2147483647", 2147483647d)]
    [InlineData("U", "0", 0d)]
    [InlineData("U", "-0.0", 0d)]
    [InlineData("U", "1", 1d)]
    [InlineData("U", "4294967295", 4294967295d)]
    [InlineData("D", "16777217", 16777217d)]
    [InlineData("D", "1+2^-40", 1d + 1d / 1099511627776d)]
    [InlineData("D", "1.7976931348623157e308", double.MaxValue)]
    [InlineData("D", "0/0", double.NaN)]
    [InlineData("D", "1/0", double.PositiveInfinity)]
    [InlineData("D", "-1/0", double.NegativeInfinity)]
    public void LuaEcsBindings_AcceptsExactNumericBoundaries(string field, string expression, double expected)
    {
        using var f = new Fixture($"p.{field}={expression}; v.{field}=p.{field}; assert(type(v.{field})=='number')");
        int entity = f.Entity(); f.Runtime.InvokeSystem(0, 0.02);
        f.Inspect(() => Assert.Equal(expected, f.Read(entity, field)));
        var p = f.World.GetPool<Scalars>().Get(entity);
        Assert.Equal(expected, field switch { "I" => p.I, "U" => p.U, _ => p.D });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LuaEcsBindings_BooleanIsNotTruthiness(bool value)
    {
        using var f = new Fixture($"p.B={value.ToString().ToLowerInvariant()}; v.B=p.B; assert(type(v.B)=='boolean')");
        int entity = f.Entity(); f.Runtime.InvokeSystem(0, 0.02);
        f.Inspect(() => Assert.Equal(value, f.Values.Get(entity).ReadBool(0)));
    }

    public static IEnumerable<object[]> InvalidAssignments()
    {
        foreach (string target in new[] { "p", "v" })
        {
            foreach (string expression in new[] { "-2147483649", "2147483648", "2147483647.5", "0.5", "0/0", "1/0", "-1/0", "'12'", "true", "nil", "{}" })
                yield return [target, "I", expression, "I32"];
            foreach (string expression in new[] { "-1", "4294967296", "4294967295.5", "0.5", "0/0", "1/0", "-1/0", "'12'", "true", "nil", "{}" })
                yield return [target, "U", expression, "U32"];
            foreach (string expression in new[] { "0", "1", "nil", "'true'", "{}" }) yield return [target, "B", expression, "Bool"];
            foreach (string expression in new[] { "'12'", "true", "nil", "{}" }) yield return [target, "D", expression, "F64"];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidAssignments))]
    public void LuaEcsBindings_InvalidTypedSetterPreservesField(string target, string field, string expression, string kind)
    {
        string sentinel = field == "B" ? "true" : "123";
        using var f = new Fixture($"p.{field}={sentinel}; v.{field}={sentinel}; {target}.{field}={expression}");
        int entity = f.Entity();
        var error = Assert.Throws<ScriptRuntimeException>(() => f.Runtime.InvokeSystem(0, 0.02));
        Assert.Contains("weather:copy", error.Message); Assert.Contains("query 'q'", error.Message);
        Assert.Contains(target == "p" ? "game:Scalars" : "weather:Values", error.Message);
        Assert.Contains($"'{field}'", error.Message); Assert.Contains(kind, error.Message);
        f.Inspect(() => { if (field == "B") Assert.True(f.Values.Get(entity).ReadBool(0)); else Assert.Equal(123d, f.Read(entity, field)); });
        var p = f.World.GetPool<Scalars>().Get(entity);
        if (field == "B") Assert.True(p.B); else Assert.Equal(123d, field switch { "I" => p.I, "U" => p.U, _ => p.D });
    }

    [Fact]
    public void LuaEcsBindings_FloatingSettersPreserveNegativeZero()
    {
        using var f = new Fixture("p.D=-0.0;v.D=p.D;p.F=-0.0;v.F=p.F");
        int entity = f.Entity(); f.Runtime.InvokeSystem(0, 0.02);
        f.Inspect(() =>
        {
            Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(f.Values.Get(entity).ReadF64(1)));
            Assert.Equal(int.MinValue, BitConverter.SingleToInt32Bits(f.Values.Get(entity).ReadF32(2)));
        });
    }

    [Theory]
    [InlineData("BOOL")]
    [InlineData("i64")]
    [InlineData("f16")]
    public void LuaEcsBindings_UnknownSchemaKindsFailBeforeCallback(string token)
    {
        var script = new Script();
        Assert.Throws<InvalidDataException>(() => new LuaScriptRuntime(script,
            [script.DoString(Source($"Value='{token}'", "error('must not run')")).Table], "weather", ["main.lua"]));
    }

    [Fact]
    public void LuaEcsBindings_ConflictingEntryKindsDoNotFreezeOrReplaceCatalog()
    {
        var script = new Script();
        var first = script.DoString("return {components={Values={version=1,fields={Value='i32'}}}}").Table;
        var other = script.DoString("return {components={Values={version=1,fields={Value='f64'}}}}").Table;
        using var conflicting = new LuaScriptRuntime(script, [first, other], "weather", ["first.lua", "other.lua"]);
        var schemas = new ComponentSchemaRegistry();
        Assert.Throws<InvalidDataException>(() => schemas.ValidateAndFreeze(conflicting.Components)); Assert.False(schemas.IsFrozen);
        using var accepted = new LuaScriptRuntime(script, [first], "weather", ["first.lua"]);
        schemas.ValidateAndFreeze(accepted.Components);
        using var recreated = new LuaScriptRuntime(script, [other], "weather", ["other.lua"]);
        Assert.Throws<InvalidDataException>(() => schemas.ValidateAndFreeze(recreated.Components));
        Assert.Equal(SchemaScalarKind.I32, schemas.GetSchemas()[0].Fields[0].Kind);
    }

    [Fact]
    public void LuaEcsBindings_ReadonlyNativeIntegerFieldCannotWrite()
    {
        using var f = new Fixture("p.ReadOnly=123"); int entity = f.Entity();
        var error = Assert.Throws<ScriptRuntimeException>(() => f.Runtime.InvokeSystem(0, 0.02));
        Assert.Contains("ReadOnly", error.Message); Assert.Equal(0, f.World.GetPool<Scalars>().Get(entity).ReadOnly);
    }

    [Theory]
    [InlineData(ScriptSystemErrorPolicy.StopSimulation)]
    [InlineData(ScriptSystemErrorPolicy.LogAndContinue)]
    public void LuaEcsBindings_TypedErrorPoliciesPreservePartialWritesAndReleaseGate(ScriptSystemErrorPolicy policy)
    {
        using var f = new Fixture("p.I=123;v.I=123;v.U=0.5", policy: policy,
            extraSystem: ", {id='later',phase='update',callback=function(dt,ctx) later=(later or 0)+1 end}");
        int entity = f.Entity();
        if (policy == ScriptSystemErrorPolicy.StopSimulation)
            Assert.Throws<ScriptRuntimeException>(() => f.Runtime.InvokeSystem(0, 0.02));
        else { f.Runtime.InvokeSystem(0, 0.02); f.Runtime.InvokeSystem(1, 0.02); Assert.Equal(1d, f.Script.Globals.Get("later").Number); }
        f.Inspect(() => { Assert.Equal(123, f.Values.Get(entity).ReadI32(3)); Assert.Equal(0u, f.Values.Get(entity).ReadU32(4)); f.Values.Del(entity); });
        if (policy == ScriptSystemErrorPolicy.LogAndContinue)
        { f.Runtime.InvokeSystem(0, 0.02); f.Runtime.InvokeSystem(1, 0.02); Assert.Equal(2d, f.Script.Globals.Get("later").Number); }
    }
}
