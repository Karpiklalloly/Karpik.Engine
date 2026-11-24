using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding.Lua;
using Microsoft.Extensions.Logging.Abstractions;
using MoonSharp.Interpreter;
using Xunit;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;

public sealed class LuaStructuralCommandTests
{
    public struct Position : IEcsComponent { public float X; }
    public struct Tag : IEcsComponent { public int Value; }

    private sealed class Fixture : IDisposable
    {
        public readonly Script Script = new();
        public readonly EcsDefaultWorld World = new();
        public readonly ComponentCallbackScope Scope = new();
        public readonly ComponentQueryPreparer Preparer;
        public readonly LuaScriptRuntime Runtime;

        public Fixture(string callback, ScriptSystemErrorPolicy policy = ScriptSystemErrorPolicy.StopSimulation,
            StructuralCommandOptions? commands = null, string? fields = null, bool readonlyPosition = false,
            string? sourceOverride = null)
        {
            LuaExactValues.Register(Script);
            string source = """
                return {components={Temperature={version=1,fields={Value="f64"}}},systems={{id="spawn",phase="update",
                  queries={all={opt={["game:Position"]={readonly=false},["game:Tag"]={readonly=false},["probe:Temperature"]={readonly=false}}}},
                  callback=function(dt,ctx)
                    local q=ctx.queries.all
                    local p=q.components["game:Position"]
                    local tag=q.components["game:Tag"]
                    local t=q.components["probe:Temperature"]
                """ + callback + "\nend}}}";
            if (fields is not null) source = source.Replace("fields={Value=\"f64\"}", "fields={" + fields + "}");
            if (readonlyPosition) source = source.Replace("[\"game:Position\"]={readonly=false}", "[\"game:Position\"]={}");
            source = sourceOverride ?? source;
            Runtime = new LuaScriptRuntime(Script, [Script.DoString(source).Table], "probe", ["Shared/main.lua"]);
            var clr = new ClrComponentBindings([new GeneratedBindings()]);
            clr.Register<Position>("game:Position"); clr.Register<Tag>("game:Tag");
            var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze(Runtime.Components);
            Preparer = new ComponentQueryPreparer(World, clr, schemas, Scope); Preparer.Initialize();
            Runtime.PrepareEcs(Preparer, Scope, new ScriptSystemOptions
            {
                ErrorPolicy = policy, Commands = commands ?? new()
            }, NullLogger.Instance);
        }

        public int Entity(float x = 0)
        {
            int entity = World.NewEntity(); World.GetPool<Position>().Add(entity).X = x; return entity;
        }
        public void Run() => Runtime.InvokeSystem(0, .02);
        public void Dispose() { Runtime.Dispose(); Preparer.Dispose(); Scope.Dispose(); World.Destroy(); }
    }

    [Fact]
    public void LuaStructuralCommand_CreatesAfterCallbackAndCapturesSnapshotAtEnqueue()
    {
        using var f = new Fixture("""
            local s=p:new_snapshot(); s.X=7
            local a=ctx.commands:create(); ctx.commands:add(p,a,s)
            s.X=19
            local b=ctx.commands:create(); ctx.commands:add(p,b,s)
            ctx.commands:add(t,b)
            s.X=99
            seen=0; for e in q:entities() do seen=seen+1 end
            """);
        f.Run();
        Assert.Equal(0, f.Script.Globals.Get("seen").Number);
        var pool = f.World.GetPool<Position>();
        Assert.Equal(2, pool.Count);
        Assert.Equal(new[] { 7f, 19f }, pool.ToSpan().ToArray().Select(e => pool.Read(e).X).Order().ToArray());
    }

    [Theory]
    [InlineData(ScriptSystemErrorPolicy.StopSimulation)]
    [InlineData(ScriptSystemErrorPolicy.LogAndContinue)]
    public void LuaStructuralCommand_CallbackFailureDiscardsQueueAndKeepsImmediateWrite(ScriptSystemErrorPolicy policy)
    {
        using var f = new Fixture("""
            for e in q:entities() do p:get(e).X=42; break end
            local a=ctx.commands:create(); ctx.commands:add(p,a)
            error('callback failed')
            """, policy);
        int entity = f.Entity();
        if (policy == ScriptSystemErrorPolicy.StopSimulation) Assert.ThrowsAny<Exception>(f.Run);
        else f.Run();
        Assert.Equal(1, f.World.Count); Assert.Equal(42, f.World.GetPool<Position>().Read(entity).X);
    }

    [Fact]
    public void LuaStructuralCommand_CaughtApiErrorPoisonsWholeBatch()
    {
        using var f = new Fixture("""
            local a=ctx.commands:create(); ctx.commands:add(p,a)
            caught=not pcall(function() ctx.commands:add(p,a,t:new_snapshot()) end)
            """, ScriptSystemErrorPolicy.LogAndContinue);
        f.Run();
        Assert.True(f.Script.Globals.Get("caught").Boolean);
        Assert.Equal(0, f.World.Count);
    }

    [Fact]
    public void LuaStructuralCommand_SavedTokenExpiresAndQueueRecoversNextCallback()
    {
        using var f = new Fixture("""
            invocation=(invocation or 0)+1
            local a=ctx.commands:create(); ctx.commands:add(p,a)
            if invocation==1 then saved=a end
            if invocation==2 then pcall(function() ctx.commands:add(tag,saved) end) end
            """, ScriptSystemErrorPolicy.LogAndContinue);
        f.Run(); Assert.Equal(1, f.World.Count);
        f.Run(); Assert.Equal(1, f.World.Count);
        f.Run(); Assert.Equal(2, f.World.Count);
    }

    [Theory]
    [InlineData("ctx.commands:add(p,a,t:new_snapshot())")]
    [InlineData("ctx.commands:add({},a)")]
    [InlineData("ctx.commands:add(p,{})")]
    [InlineData("ctx.commands:add(p,1.5)")]
    [InlineData("ctx.commands:add(p,0/0)")]
    [InlineData("ctx.commands:add(p,nil)")]
    [InlineData("ctx.commands.add(p,a)")]
    [InlineData("ctx.commands:unknown()")]
    [InlineData("ctx.commands.add=42")]
    public void LuaStructuralCommand_CaughtMalformedOperationDiscardsEarlierValidCommands(string operation)
    {
        using var f = new Fixture("local a=ctx.commands:create(); ctx.commands:add(p,a); " +
            "caught=not pcall(function() " + operation + " end)", ScriptSystemErrorPolicy.LogAndContinue);
        f.Run();
        Assert.True(f.Script.Globals.Get("caught").Boolean);
        Assert.Equal(0, f.World.Count);
    }

    [Fact]
    public void LuaStructuralCommand_ReadonlyHandleCannotInitializeNewEntity()
    {
        using var f = new Fixture("local a=ctx.commands:create(); ctx.commands:add(p,a)",
            ScriptSystemErrorPolicy.LogAndContinue, readonlyPosition: true);
        f.Run(); Assert.Equal(0, f.World.Count);
    }

    [Fact]
    public void LuaStructuralCommand_OverflowCaughtByLuaStillDiscardsBatch()
    {
        using var f = new Fixture("""
            local a=ctx.commands:create(); ctx.commands:add(p,a)
            caught=not pcall(function() ctx.commands:create() end)
            """, ScriptSystemErrorPolicy.LogAndContinue,
            commands: new() { MaxCommands = 2, MaxPendingEntities = 1 });
        f.Run(); Assert.True(f.Script.Globals.Get("caught").Boolean); Assert.Equal(0, f.World.Count);
    }

    [Fact]
    public void LuaStructuralCommand_LastComponentDelRejectsWholeBatchButReplacementWorks()
    {
        using var f = new Fixture("""
            invocation=(invocation or 0)+1
            for e in q:entities() do
              if invocation==1 then ctx.commands:del(p,e)
              else ctx.commands:add(tag,e); ctx.commands:del(p,e) end
            end
            """, ScriptSystemErrorPolicy.LogAndContinue);
        int entity = f.Entity(5);
        f.Run(); Assert.Equal(5, f.World.GetPool<Position>().Read(entity).X);
        f.Run(); Assert.False(f.World.GetPool<Position>().Has(entity));
        Assert.True(f.World.IsUsed(entity)); Assert.True(f.World.GetPool<Tag>().Has(entity));
    }

    [Fact]
    public void LuaStructuralCommand_CrossSystemSavedCommandAccessCannotCreateForCurrentOwner()
    {
        const string source = """
            local saved
            return {systems={
              {id='a',phase='update',callback=function(dt,ctx) saved=ctx.commands end},
              {id='b',phase='update',after={'probe:a'},queries={q={opt={['game:Position']={readonly=false}}}},
                callback=function(dt,ctx)
                  local p=ctx.queries.q.components['game:Position']
                  local a=ctx.commands:create(); ctx.commands:add(p,a)
                  caught=not pcall(function() saved:create() end)
                end}
            }}
            """;
        using var f = new Fixture("", ScriptSystemErrorPolicy.LogAndContinue, sourceOverride: source);
        f.Runtime.InvokeSystem(0, .02); f.Runtime.InvokeSystem(1, .02);
        Assert.True(f.Script.Globals.Get("caught").Boolean); Assert.Equal(0, f.World.Count);
    }

    [Fact]
    public void LuaStructuralCommand_InitializesExactNestedFixedAndDefaultDynamicValues()
    {
        using var f = new Fixture("""
            local s=t:new_snapshot()
            s.N.I=types.i64('9007199254740993'); s.N.U=types.u64('18446744073709551615')
            s.N.D=types.decimal('0.1'); s.N.C=55296; s.N.B=true
            s.Points[1]=3.25; s.Points[2]=7.5
            local a=ctx.commands:create(); ctx.commands:add(t,a,s)
            local b=ctx.commands:create(); ctx.commands:add(t,b)
            s.Points[1]=99; s.N.I=types.i64('0')
            """, fields: "N={kind='struct',fields={I='i64',U='u64',D='decimal',C='char',B='bool'}},Points={kind='fixed',length=2,element='f64'}");
        f.Run();
        var query = f.Preparer.Prepare(new("inspect", [], [], [new("probe:Temperature")]), "inspect");
        var handle = query.Components["probe:Temperature"];
        f.Scope.Enter("inspect");
        try
        {
            query.Capture(); Assert.True(query.MoveNext(out int entity));
            var snapshot = handle.Copy(entity);
            if (!snapshot.ReadBool(Field("N.B")))
            { Assert.True(query.MoveNext(out entity)); snapshot = handle.Copy(entity); }
            Assert.True(snapshot.ReadBool(Field("N.B")));
            Assert.Equal(9007199254740993L, snapshot.ReadI64(Field("N.I")));
            Assert.Equal(ulong.MaxValue, snapshot.ReadU64(Field("N.U")));
            Assert.Equal(.1m, snapshot.ReadDecimal(Field("N.D")));
            Assert.Equal((char)55296, snapshot.ReadChar(Field("N.C")));
            Assert.Equal(3.25, snapshot.ReadF64(Field("Points[1]")));
            Assert.Equal(7.5, snapshot.ReadF64(Field("Points[2]")));
            int Field(string name)
            {
                for (int i = 0; i < handle.Fields.Length; i++) if (handle.Fields.Span[i].Name == name) return i;
                throw new InvalidOperationException("Missing field " + name);
            }
        }
        finally { f.Scope.Exit(); }
        Assert.Equal(2, f.World.Count);
    }
}
