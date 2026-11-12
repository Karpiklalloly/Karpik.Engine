using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Microsoft.Extensions.Logging.Abstractions;
using MoonSharp.Interpreter;
using Xunit;

public sealed class LuaCompositeBindingsTests
{
    [Fact]
    public void InlineNestedFixedAndEnumFieldsRoundTripThroughLua()
    {
        using var f = new LuaCompositeFixture("""
            local pool=ctx.queries.q.components['test:Shape']
            for entity in ctx.queries.q:entities() do
                local value=pool:get(entity)
                assert(#value.Values==3 and #value.Points==2)
                value.Position.X=1.25; value.Position.Y=16777217
                value.Values[1]=-17; value.Values[3]=42
                value.Points[1].X=11; value.Points[1].Y=2.5
                value.Points[2].X=-12; value.Points[2].Y=3.25
                value.Flag=4294967295
                value.Wide=types.i64('9223372036854775807')
            end
            """);
        int entity = f.Entity(); f.Runtime.InvokeSystem(0, 0.02);
        f.Inspect(() =>
        {
            Assert.Equal(1.25f, f.Source.Get(entity).Read<float>(f.Field("Position.X")));
            Assert.Equal(16777217d, f.Source.Get(entity).Read<double>(f.Field("Position.Y")));
            Assert.Equal(-17, f.Source.Get(entity).Read<int>(f.Field("Values[1]")));
            Assert.Equal(42, f.Source.Get(entity).Read<int>(f.Field("Values[3]")));
            Assert.Equal(11, f.Source.Get(entity).Read<int>(f.Field("Points[1].X")));
            Assert.Equal(3.25d, f.Source.Get(entity).Read<double>(f.Field("Points[2].Y")));
            Assert.Equal(uint.MaxValue, f.Source.Get(entity).Read<uint>(f.Field("Flag")));
            Assert.Equal(long.MaxValue, f.Source.Get(entity).Read<long>(f.Field("Wide")));
        });
    }

    public static IEnumerable<object[]> InvalidIndices()
    {
        foreach (string index in new[] { "0", "4", "1.5", "0/0", "1/0", "'1'", "true", "nil" })
        {
            yield return [index, false];
            yield return [index, true];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidIndices))]
    public void FixedArrayRejectsInvalidReadAndWriteIndicesWithoutChangingData(string index, bool write)
    {
        string access = write ? $"value.Values[{index}]=99" : $"local ignored=value.Values[{index}]";
        using var f = new LuaCompositeFixture("local value=ctx.queries.q.components['test:Shape']:get(first);" + access);
        int entity = f.Entity(); f.Script.Globals.Set("first", DynValue.NewNumber(entity));
        f.Inspect(() => f.Source.Get(entity).Write(f.Field("Values[1]"), 7));
        Exception error = Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(0, 0.02));
        Assert.Contains("Values", error.Message);
        f.Inspect(() =>
        {
            Assert.Equal(7, f.Source.Get(entity).Read<int>(f.Field("Values[1]")));
            Assert.Equal(0, f.Source.Get(entity).Read<int>(f.Field("Values[2]")));
            Assert.Equal(0, f.Source.Get(entity).Read<int>(f.Field("Values[3]")));
        });
    }

    [Theory]
    [InlineData("Position.X", "1.25")]
    [InlineData("Values[1]", "17")]
    [InlineData("Points[1].X", "18")]
    public void ReadonlyQueryBlocksNestedAndIndexedWrites(string path, string value)
    {
        using var f = new LuaCompositeFixture($"ctx.queries.q.components['test:Shape']:get(first).{path}={value}", readOnly: true);
        int entity = f.Entity(); f.Script.Globals.Set("first", DynValue.NewNumber(entity));
        Exception error = Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(0, 0.02));
        Assert.Contains("readonly", error.Message, StringComparison.OrdinalIgnoreCase);
        f.Inspect(() =>
        {
            Assert.Equal(0f, f.Source.Get(entity).Read<float>(f.Field("Position.X")));
            Assert.Equal(0, f.Source.Get(entity).Read<int>(f.Field("Values[1]")));
            Assert.Equal(0, f.Source.Get(entity).Read<int>(f.Field("Points[1].X")));
        });
    }

    [Fact]
    public void NestedLiveViewsAliasButSnapshotViewsStayIndependentAndCopyToReusesIdentity()
    {
        using var f = new LuaCompositeFixture("""
            local pool=ctx.queries.q.components['test:Shape']
            local live=pool:get(first).Position
            saved=pool:copy(first)
            local savedPosition=saved.Position
            local savedPoint=saved.Points[1]
            pool:get(second)
            assert(live.X==2 and savedPosition.X==1 and savedPoint.X==11)
            local buffer=pool:new_snapshot()
            local bufferedPosition=buffer.Position
            local bufferedPoints=buffer.Points
            local bufferedPoint=buffer.Points[1]
            assert(bufferedPosition.X==0 and bufferedPoint.X==0)
            assert(rawequal(buffer,pool:copy_to(first,buffer)))
            assert(rawequal(bufferedPosition,buffer.Position) and rawequal(bufferedPoints,buffer.Points))
            assert(rawequal(bufferedPoint,buffer.Points[1]) and bufferedPosition.X==1 and bufferedPoint.X==11)
            pool:copy_to(second,buffer)
            assert(bufferedPosition.X==2 and bufferedPoint.X==22)
            saved.Position.X=99; saved.Points[1].X=77
            assert(pool:get(first).Position.X==1 and pool:get(first).Points[1].X==11)
            """);
        int first = f.Entity(), second = f.Entity();
        f.Script.Globals.Set("first", DynValue.NewNumber(first)); f.Script.Globals.Set("second", DynValue.NewNumber(second));
        f.Inspect(() =>
        {
            f.Source.Get(first).Write(f.Field("Position.X"), 1f); f.Source.Get(first).Write(f.Field("Points[1].X"), 11);
            f.Source.Get(second).Write(f.Field("Position.X"), 2f); f.Source.Get(second).Write(f.Field("Points[1].X"), 22);
        });
        f.Runtime.InvokeSystem(0, 0.02);
        Assert.Equal(99d, f.Script.DoString("return saved.Position.X").Number);
    }

    [Fact]
    public void SnapshotSurvivesLaterCallbacksAndSourceDeletionButNotSimulationTeardown()
    {
        var f = new LuaCompositeFixture("""
            if not saved then
                saved=ctx.queries.q.components['test:Shape']:copy(first)
                savedPosition=saved.Position
            else
                assert(savedPosition.X==3.5 and saved.Points[2].X==27)
                savedPosition.X=4.5
            end
            """);
        try
        {
            int entity = f.Entity(); f.Script.Globals.Set("first", DynValue.NewNumber(entity));
            f.Inspect(() =>
            {
                f.Source.Get(entity).Write(f.Field("Position.X"), 3.5f);
                f.Source.Get(entity).Write(f.Field("Points[2].X"), 27);
            });
            f.Runtime.InvokeSystem(0, 0.02);
            f.Inspect(() => f.Source.Del(entity)); f.World.ReleaseDelEntityBufferAll();
            f.Runtime.InvokeSystem(0, 0.02);
            Assert.Equal(4.5d, f.Script.DoString("return savedPosition.X").Number);
            f.Dispose();
            Assert.ThrowsAny<Exception>(() => f.Script.DoString("return savedPosition.X"));
        }
        finally { f.Dispose(); }
    }

    [Fact]
    public void SnapshotFromReadonlySourceAllowsDetachedMutationOnly()
    {
        using var f = new LuaCompositeFixture("""
            local pool=ctx.queries.q.components['test:Shape']
            local copy=pool:copy(first)
            copy.Position.X=99
            assert(copy.Position.X==99 and pool:get(first).Position.X==0)
            """, readOnly: true);
        int entity = f.Entity(); f.Script.Globals.Set("first", DynValue.NewNumber(entity));
        f.Runtime.InvokeSystem(0, 0.02);
    }

    [Fact]
    public void CopyToRejectsAnotherComponentBeforeChangingSnapshot()
    {
        using var f = new LuaCompositeFixture("""
            local q=ctx.queries.q
            saved=q.components['test:Shape']:copy(first)
            q.components['test:Other']:copy_to(first,saved)
            """);
        int entity = f.Entity(withDestination: true); f.Script.Globals.Set("first", DynValue.NewNumber(entity));
        f.Inspect(() =>
        {
            f.Source.Get(entity).Write(f.Field("Position.X"), 1f);
            f.Destination.Get(entity).Write(f.Field("Position.X"), 2f);
        });
        Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(0, 0.02));
        Assert.Equal(1d, f.Script.DoString("return saved.Position.X").Number);
    }

    [Fact]
    public void PointerSchemaRequiresHostPermissionAndSnapshotsKeepRevocationChecks()
    {
        const string fields = "Pointer={kind='pointer',type='host:Int'}";
        Assert.Throws<InvalidDataException>(() => new LuaCompositeFixture("", fields));
        using var pointers = new ComponentPointerRegistry();
        var domain = pointers.CreateDomain(); var type = pointers.RegisterType("host:Int", typeof(int).MakePointerType(), domain);
        using var f = new LuaCompositeFixture("""
            if not saved then
                local pool=ctx.queries.q.components['test:Shape']
                local empty=pool:new_snapshot(); assert(empty.Pointer==nil)
                saved=pool:copy(first)
                local value=pool:get(first)
                value.Pointer=nil; assert(value.Pointer==nil)
                value.Pointer=saved.Pointer
                assert(type(value.Pointer)=='userdata')
            else
                local ignored=saved.Pointer
            end
            """, fields, pointers: pointers);
        int entity = f.Entity(); f.Script.Globals.Set("first", DynValue.NewNumber(entity));
        f.Inspect(() => f.Source.Get(entity).WritePointer(0, pointers.Capture(type, 42)));
        f.Runtime.InvokeSystem(0, 0.02);
        f.Inspect(() => Assert.Equal((nuint)42, pointers.ReadAddress(f.Source.Get(entity).ReadPointer(0), type)));
        pointers.Revoke(domain);
        Exception error = Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(0, 0.02));
        Assert.Contains("revoked", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class LuaCompositeFixture : IDisposable
{
    internal const string DefaultFields = """
        Position={kind='struct',fields={X='f32',Y='f64'}},
        Values={kind='fixed',element='i32',length=3},
        Points={kind='fixed',element={kind='struct',fields={X='i32',Y='f64'}},length=2},
        Flag={kind='enum',underlying='u32'},Wide='i64'
        """;
    internal readonly Script Script = new();
    internal readonly EcsDefaultWorld World = new();
    internal readonly ComponentCallbackScope Scope = new();
    internal LuaScriptRuntime Runtime { get; private set; } = null!;
    internal ComponentQueryPreparer Preparer { get; private set; } = null!;
    internal PreparedComponentHandle Source { get; private set; } = null!;
    internal PreparedComponentHandle Destination { get; private set; } = null!;
    private bool _disposed;

    internal LuaCompositeFixture(string body, string fields = DefaultFields, bool readOnly = false,
        ComponentPointerRegistry? pointers = null, string preamble = "")
    {
        try
        {
            LuaExactValues.Register(Script);
            string source = """
                __PREAMBLE__
                return {components={Shape={version=1,fields={__FIELDS__}},Other={version=1,fields={__FIELDS__}}},
                systems={{id='run',phase='update',queries={q={inc={['test:Shape']={readonly=__READONLY__}},
                opt={['test:Other']={readonly=false}}}},callback=function(dt,ctx)
                __BODY__
                end}}}
                """.Replace("__PREAMBLE__", preamble, StringComparison.Ordinal)
                .Replace("__FIELDS__", fields, StringComparison.Ordinal)
                .Replace("__READONLY__", readOnly ? "true" : "false", StringComparison.Ordinal)
                .Replace("__BODY__", body, StringComparison.Ordinal);
            Runtime = new(Script, [Script.DoString(source).Table], "test", ["main.lua"]);
            var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze(Runtime.Components);
            Preparer = new(World, new ClrComponentBindings([]), schemas, Scope, pointers); Preparer.Initialize();
            Runtime.PrepareEcs(Preparer, Scope, new ScriptSystemOptions(), NullLogger.Instance);
            var inspection = Preparer.Prepare(new("inspect", [], [], [new("test:Shape", false), new("test:Other", false)]), "inspect");
            Source = inspection.Components["test:Shape"]; Destination = inspection.Components["test:Other"];
        }
        catch { Dispose(); throw; }
    }
    internal int Entity(bool withDestination = false)
    {
        int entity = World.NewEntity();
        Inspect(() => { Source.Add(entity); if (withDestination) Destination.Add(entity); });
        return entity;
    }
    internal int Field(string name)
    {
        for (int i = 0; i < Source.Fields.Length; i++) if (Source.Fields.Span[i].Name == name) return i;
        throw new InvalidOperationException(name);
    }
    internal void Inspect(Action action)
    {
        Scope.Enter("inspect"); try { action(); } finally { Scope.Exit(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        Runtime?.Dispose(); Preparer?.Dispose(); Scope.Dispose(); World.Destroy();
    }
}
