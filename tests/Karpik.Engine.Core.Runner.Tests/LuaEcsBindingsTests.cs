using Karpik.Engine.Shared.Modding.Lua;
using MoonSharp.Interpreter;
using Xunit;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Karpik.Engine.Shared.Modding;
using Microsoft.Extensions.Logging.Abstractions;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;
using Xunit.Abstractions;
using System.Diagnostics;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Modding.Lua.Systems;

[Collection("Lua ECS allocation probes")]
public sealed class LuaEcsBindingsTests(ITestOutputHelper output)
{
    public struct Position : IEcsComponent { public float X, Y; }
    private const string Slice = """
        return {components={Temperature={version=1,fields={Value="f32"}}},systems={{id="init",phase="update",
          queries={pending={inc={["game:Position"]={}},exc={["weather:Temperature"]={readonly=false}}}},
          callback=function(dt,ctx)
            local q=ctx.queries.pending
            local positions=q.components["game:Position"]
            local temperatures=q.components["weather:Temperature"]
            for e in q:entities() do local p=positions:get(e); local t=temperatures:add(e); t.Value=20-p.Y*0.01 end
          end}}}
        """;

    private sealed class Fixture : IDisposable
    {
        public readonly Script Script = new();
        public readonly EcsDefaultWorld World = new();
        public readonly ComponentCallbackScope Scope = new();
        public readonly LuaScriptRuntime Runtime;
        public readonly ComponentQueryPreparer Preparer;
        public Fixture(string source = Slice, ScriptSystemErrorPolicy policy = ScriptSystemErrorPolicy.StopSimulation)
        {
            Runtime = new LuaScriptRuntime(Script, [Script.DoString(source).Table], "weather", ["Shared/main.lua"]);
            var clr = new ClrComponentBindings([new GeneratedBindings()]); clr.Register<Position>("game:Position");
            var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze(Runtime.Components);
            Preparer = new ComponentQueryPreparer(World, clr, schemas, Scope); Preparer.Initialize();
            Runtime.PrepareEcs(Preparer, Scope, new ScriptSystemOptions { ErrorPolicy = policy }, NullLogger.Instance);
        }
        public int Entity(float y)
        {
            var pool = World.GetPool<Position>(); int entity = World.NewEntity(); pool.Add(entity).Y = y; return entity;
        }
        public PreparedComponentHandle Temperatures => Runtime.GetContext(0).Queries[0].Components["weather:Temperature"];
        public void Inspect(Action action) { Scope.Enter("weather:init"); try { action(); } finally { Scope.Exit(); } }
        public void ResetTemperatures(ReadOnlySpan<int> entities)
        {
            Scope.Enter("weather:init");
            try { var pool = Temperatures; foreach (int entity in entities) if (pool.Has(entity)) pool.Del(entity); }
            finally { Scope.Exit(); }
        }
        public void Dispose() { Runtime.Dispose(); Preparer.Dispose(); Scope.Dispose(); World.Destroy(); }
    }

    [Fact]
    public void LuaEcsBindings_EndToEndMixedQueryAndDefaultMembership()
    {
        using var f = new Fixture(); int a = f.Entity(0), b = f.Entity(100), c = f.Entity(200);
        f.Inspect(() => f.Temperatures.Add(c).WriteF32(0, 99));
        f.Runtime.InvokeSystem(0, 0.02);
        f.Inspect(() => { Assert.Equal(20, f.Temperatures.Get(a).ReadF32(0)); Assert.Equal(19, f.Temperatures.Get(b).ReadF32(0)); Assert.Equal(99, f.Temperatures.Get(c).ReadF32(0)); });
        f.Runtime.InvokeSystem(0, 0.02);
        Assert.Equal(100, f.World.GetPool<Position>().Get(b).Y);
    }

    [Theory]
    [InlineData("1/0", float.PositiveInfinity)]
    [InlineData("-1/0", float.NegativeInfinity)]
    [InlineData("0/0", float.NaN)]
    public void LuaEcsBindings_F32PreservesNonFiniteValues(string expression, float expected)
    {
        using var f = new Fixture(Slice.Replace("20-p.Y*0.01", expression)); int entity = f.Entity(0);
        f.Runtime.InvokeSystem(0, 0.02); f.Inspect(() => Assert.Equal(expected, f.Temperatures.Get(entity).ReadF32(0)));
    }

    [Theory]
    [InlineData("1e100")]
    [InlineData("'text'")]
    [InlineData("true")]
    [InlineData("nil")]
    public void LuaEcsBindings_InvalidSetterKeepsPartialAddAndReleasesGate(string expression)
    {
        using var f = new Fixture(Slice.Replace("20-p.Y*0.01", expression)); int entity = f.Entity(0);
        Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(0, 0.02));
        f.Inspect(() => Assert.Equal(0, f.Temperatures.Get(entity).ReadF32(0)));
        f.Runtime.InvokeSystem(0, 0.02);
    }

    [Theory]
    [InlineData(ScriptSystemErrorPolicy.StopSimulation)]
    [InlineData(ScriptSystemErrorPolicy.LogAndContinue)]
    public void LuaEcsBindings_ErrorPolicyPreservesWritesAndRetriesNextCallback(ScriptSystemErrorPolicy policy)
    {
        using var f = new Fixture(Slice.Replace("t.Value=20-p.Y*0.01", "t.Value=7; error('intentional')"), policy);
        int a = f.Entity(0), b = f.Entity(1);
        if (policy == ScriptSystemErrorPolicy.StopSimulation) Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(0, 0.02));
        else f.Runtime.InvokeSystem(0, 0.02);
        int completed = 0; f.Inspect(() => { foreach (int e in new[] { a, b }) if (f.Temperatures.Has(e)) { completed++; Assert.Equal(7, f.Temperatures.Get(e).ReadF32(0)); } });
        Assert.Equal(1, completed);
        if (policy == ScriptSystemErrorPolicy.StopSimulation) Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(0, 0.02));
        else f.Runtime.InvokeSystem(0, 0.02);
        f.Inspect(() => { Assert.True(f.Temperatures.Has(a)); Assert.True(f.Temperatures.Has(b)); });
    }
    [Fact]
    public void LuaEcsBindings_AcceptsQueriesAndComponentDeclarations()
    {
        var script = new Script();
        Table entry = script.DoString("""
            return { components={Temperature={version=1,fields={Value="f32"}}}, systems={{
                id="init",phase="update",queries={pending={inc={["game:Position"]={}},exc={["weather:Temperature"]={readonly=false}}}},
                callback=function(dt,ctx) end }} }
            """).Table;
        using var runtime = new LuaScriptRuntime(script, [entry], "weather", ["Shared/main.lua"]);
        Assert.Single(runtime.Systems);
    }

    [Theory]
    [InlineData("reads")]
    [InlineData("writes")]
    public void LuaEcsBindings_OldAccessListsRequireQueryMigration(string field)
    {
        var script = new Script();
        Table entry = script.DoString("return {systems={{id='init',phase='update'," + field + "={},callback=function(dt) end}}}").Table;
        var error = Assert.Throws<InvalidDataException>(() => new LuaScriptRuntime(script, [entry], "weather", ["Shared/main.lua"]));
        Assert.Contains("queries", error.Message);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0.5")]
    [InlineData("2147483648")]
    [InlineData("2147483647")]
    [InlineData("1/0")]
    [InlineData("'1'")]
    public void LuaEcsBindings_EntityIdsAreValidatedInRelease(string expression)
    {
        using var f = new Fixture(Slice.Replace("positions:get(e)", "positions:get(" + expression + ")")); f.Entity(0);
        Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(0, 0.02));
    }

    [Fact]
    public void LuaEcsBindings_PoolFailureNamesQueryAndComponent()
    {
        using var f = new Fixture(Slice.Replace("positions:get(e)", "positions:get(-1)")); f.Entity(0);
        var error = Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(0, 0.02));
        Assert.Contains("pending", error.Message);
        Assert.Contains("game:Position", error.Message);
    }

    [Fact]
    public void LuaEcsBindings_SavedPoolAndViewCannotCrossSystemOwnership()
    {
        using var f = new Fixture("""
            return {components={Temperature={version=1,fields={Value='f32'}}},systems={
              {id='init',phase='update',queries={pending={opt={['game:Position']={}}}},
               callback=function(dt,ctx) saved=ctx.queries.pending.components['game:Position']; view=saved:get(target) end},
              {id='steal',phase='update',callback=function(dt,ctx) return saved:get(target) end}
            }}
            """);
        int entity = f.Entity(7); f.Script.Globals.Set("target", DynValue.NewNumber(entity));
        f.Runtime.InvokeSystem(0, 0.02);
        Assert.ThrowsAny<Exception>(() => f.Runtime.InvokeSystem(1, 0.02));
        Assert.ThrowsAny<Exception>(() => f.Script.DoString("return view.Y"));
        f.Runtime.InvokeSystem(0, 0.02);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(256)]
    [InlineData(4096)]
    public void LuaEcsAllocation_ActualSliceStaysWithinApprovedExperimentalBudget(int count)
    {
        using var f = new Fixture(); var entities = new int[count];
        for (int i = 0; i < count; i++) entities[i] = f.Entity(i);
        for (int i = 0; i < 2000; i++) { f.ResetTemperatures(entities); f.Runtime.InvokeSystem(0, 0.02); }
        var ticks = new long[1000]; long bytes = 0;
        int collections = GC.CollectionCount(0);
        for (int i = 0; i < ticks.Length; i++)
        {
            f.ResetTemperatures(entities);
            long beginBytes = GC.GetAllocatedBytesForCurrentThread(), beginTicks = Stopwatch.GetTimestamp();
            f.Runtime.InvokeSystem(0, 0.02);
            ticks[i] = Stopwatch.GetTimestamp() - beginTicks;
            bytes += GC.GetAllocatedBytesForCurrentThread() - beginBytes;
        }
        collections = GC.CollectionCount(0) - collections;
        Assert.InRange(bytes / 1000d, 0, 4096d + 1600d * count);
        f.Inspect(() => { foreach (int entity in entities) Assert.True(f.Temperatures.Has(entity)); });
        Array.Sort(ticks); double micros = 1_000_000d / Stopwatch.Frequency;
        output.WriteLine($"Actual ECS N={count}; B/callback={bytes / 1000d:F0}; p50_us={ticks[499] * micros:F3}; p95_us={ticks[949] * micros:F3}; p99_us={ticks[989] * micros:F3}; Gen0={collections}; CLR={Environment.Version}");
    }

    [Theory]
    [InlineData(ScriptSystemErrorPolicy.StopSimulation, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(ScriptSystemErrorPolicy.StopSimulation, EcsUpdateSchedulerMode.Parallel)]
    [InlineData(ScriptSystemErrorPolicy.LogAndContinue, EcsUpdateSchedulerMode.Deterministic)]
    [InlineData(ScriptSystemErrorPolicy.LogAndContinue, EcsUpdateSchedulerMode.Parallel)]
    public void LuaEcsBindings_WorkerHonorsStopAndContinuePolicies(ScriptSystemErrorPolicy policy, EcsUpdateSchedulerMode mode)
    {
        string source = Slice.Replace("t.Value=20-p.Y*0.01", "t.Value=7; error('intentional')")
            .Replace("end}}}", "end},{id='later',phase='update',callback=function(dt,ctx) later=(later or 0)+1 end}}}");
        using var f = new Fixture(source, policy); f.Entity(0); f.Entity(1);
        using var loop = new RuntimeLoop(f, mode);
        var worker = new ClientSimulationWorker(loop);
        try
        {
            Assert.True(worker.TryReserveFrame()); worker.StartReservedFrame(0.02);
            if (policy == ScriptSystemErrorPolicy.StopSimulation)
            {
                Assert.True(SpinWait.SpinUntil(() => { try { worker.ThrowIfFaulted(); return false; } catch (ScriptRuntimeException) { return true; } }, TimeSpan.FromSeconds(5)));
                Assert.Throws<ScriptRuntimeException>(worker.ThrowIfFaulted);
                Assert.Throws<ScriptRuntimeException>(() => worker.TryReserveFrame());
                Assert.True(f.Script.Globals.Get("later").IsNil());
            }
            else
            {
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref loop.Completed) == 1, TimeSpan.FromSeconds(5)));
                Assert.Equal(1, f.Script.Globals.Get("later").Number);
                Assert.True(SpinWait.SpinUntil(worker.TryReserveFrame, TimeSpan.FromSeconds(5))); worker.StartReservedFrame(0.02);
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref loop.Completed) == 2, TimeSpan.FromSeconds(5)));
                Assert.Equal(2, f.Script.Globals.Get("later").Number); worker.ThrowIfFaulted();
            }
            Assert.True(loop.Owner.IsEnabled);
        }
        finally
        {
            if (policy == ScriptSystemErrorPolicy.StopSimulation) Assert.Throws<ScriptRuntimeException>(worker.Dispose);
            else worker.Dispose();
        }
    }

    private sealed class RuntimeLoop : IClientSimulationLoop, IDisposable
    {
        private readonly EcsUpdateScheduler _scheduler = new();
        private readonly Time _time = new();
        internal readonly ModContainer Owner;
        internal int Completed;
        public bool IsApplicationRunning => true;
        internal RuntimeLoop(Fixture fixture, EcsUpdateSchedulerMode mode)
        {
            Owner = new(new ModDefinition(new ModMetaData("weather", "Weather", "test", "1", ""), "."), fixture.Runtime);
            Owner.Load(); Owner.Start();
            var systems = new ISystemUpdate[fixture.Runtime.Systems.Length];
            var descriptors = new EcsUpdateSystemDescriptor[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i] = new LuaUpdateSystem(Owner, fixture.Runtime, i, _time);
                var declaration = fixture.Runtime.Systems[i];
                descriptors[i] = new(typeof(LuaUpdateSystem), true, fixture.Runtime.GetContext(i).Accesses, declaration.Orders, declaration.Id);
            }
            _scheduler.InitializeInstances(systems, descriptors, mode);
        }
        public void RunGameplayFrame(double deltaTime) { _time.Update(deltaTime); _scheduler.Update(); Interlocked.Increment(ref Completed); }
        public void Dispose() { _scheduler.Dispose(); Owner.Dispose(); }
    }
}
