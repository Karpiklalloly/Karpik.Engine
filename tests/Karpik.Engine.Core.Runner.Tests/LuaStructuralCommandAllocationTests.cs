using System.Diagnostics;
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
public sealed class LuaStructuralCommandAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(256)]
    [InlineData(4096)]
    public void LuaStructuralCommand_CanonicalCreatePayloadWorkloadStaysWithinApprovedBudget(int count)
    {
        using var f = new Fixture(count);
        // Snapshot factories allocate once in a real owning callback, outside measured steady state.
        f.Runtime.InvokeSystem(0, .02);
        const int warmup = 16;
        for (int i = 0; i < warmup; i++) { f.Runtime.InvokeSystem(0, .02); f.Reset(); }
        const int iterations = 32;
        long allocated = 0;
        var ticks = new long[iterations];
        int gen0 = GC.CollectionCount(0);
        long memoryBefore = GC.GetTotalMemory(false);
        int worldCapacity = f.World.Capacity;
        int nativeCapacity = f.World.GetPool<LuaStructuralCommandTests.Position>().Capacity;
        for (int i = 0; i < iterations; i++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(), begin = Stopwatch.GetTimestamp();
            f.Runtime.InvokeSystem(0, .02);
            ticks[i] = Stopwatch.GetTimestamp() - begin;
            allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
            Assert.Equal(count, f.World.Count);
            Assert.Equal(count, f.World.GetPool<LuaStructuralCommandTests.Position>().Count);
            Assert.Equal(worldCapacity, f.World.Capacity);
            Assert.Equal(nativeCapacity, f.World.GetPool<LuaStructuralCommandTests.Position>().Capacity);
            f.VerifyAndReset();
        }
        gen0 = GC.CollectionCount(0) - gen0;
        long memoryAfter = GC.GetTotalMemory(false);
        double bytesPerCallback = allocated / (double)iterations;
        // Per entity: create + native add + dynamic add, two captured scalar payload leaves, no exact arithmetic.
        long commands = 3L * count, leaves = 2L * count;
        long ceiling = 4096 + 1600 * commands + 1600 * leaves;
        Assert.InRange(bytesPerCallback, 0, ceiling);
        Array.Sort(ticks);
        double micros = 1_000_000d / Stopwatch.Frequency;
        output.WriteLine($"Structural Lua N={count}; C={commands}; V={leaves}; O=0; B/callback={bytesPerCallback:F0}; ceiling={ceiling}; " +
            $"p50_us={ticks[iterations / 2 - 1] * micros:F3}; p95_us={ticks[(int)Math.Ceiling(iterations * .95) - 1] * micros:F3}; loop_Gen0_including_cleanup={gen0}; " +
            $"managed_before={memoryBefore}; managed_after={memoryAfter}; process_peak_bytes={Process.GetCurrentProcess().PeakWorkingSet64}; CLR={Environment.Version}");
    }

    private sealed class Fixture : IDisposable
    {
        public readonly EcsDefaultWorld World;
        public readonly ComponentCallbackScope Scope = new();
        public readonly ComponentQueryPreparer Preparer;
        public readonly LuaScriptRuntime Runtime;
        private readonly PreparedComponentHandle _temperature;
        private readonly int _count;

        public Fixture(int count)
        {
            _count = count;
            World = new EcsDefaultWorld(new EcsWorldConfig(entitiesCapacity: Math.Max(16, count + 1),
                poolComponentsCapacity: Math.Max(16, count + 1)));
            var script = new Script();
            script.Globals.Set("n", DynValue.NewNumber(count));
            Runtime = new LuaScriptRuntime(script, [script.DoString("""
                local position,heat
                return {components={Temperature={version=1,fields={Value='f64'}}},systems={{id='spawn',phase='update',
                  queries={q={opt={['game:Position']={readonly=false},['probe:Temperature']={readonly=false}}}},
                  callback=function(dt,ctx)
                    local q=ctx.queries.q
                    local p=q.components['game:Position']; local t=q.components['probe:Temperature']
                    if position==nil then position=p:new_snapshot(); heat=t:new_snapshot(); return end
                    for i=1,n do
                      position.X=i; heat.Value=i+0.5
                      local entity=ctx.commands:create()
                      ctx.commands:add(p,entity,position); ctx.commands:add(t,entity,heat)
                    end
                  end}}}
                """).Table], "probe", ["Shared/main.lua"]);
            var clr = new ClrComponentBindings([new GeneratedBindings()]);
            clr.Register<LuaStructuralCommandTests.Position>("game:Position");
            var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze(Runtime.Components);
            Preparer = new ComponentQueryPreparer(World, clr, schemas, Scope); Preparer.Initialize();
            Runtime.PrepareEcs(Preparer, Scope, new ScriptSystemOptions
            {
                Commands = new StructuralCommandOptions
                {
                    MaxCommands = Math.Max(1, checked(count * 3)),
                    MaxPendingEntities = Math.Max(1, count),
                    MaxPayloadBytes = Math.Max(256 * 1024, checked(count * 32))
                }
            }, NullLogger.Instance);
            _temperature = Runtime.GetContext(0).Queries[0].Components["probe:Temperature"];
        }

        public void VerifyAndReset()
        {
            var positions = World.GetPool<LuaStructuralCommandTests.Position>();
            Assert.Equal(_count, positions.Count);
            Scope.Enter("probe:spawn");
            try
            {
                foreach (int entity in positions.ToSpan())
                    Assert.Equal((double)positions.Read(entity).X + .5, _temperature.Get(entity).ReadF64(0));
            }
            finally { Scope.Exit(); }
            Reset();
        }
        public void Reset()
        {
            // Cleanup, enumeration materialization and backend deletion release are outside the measured callback.
            int[] entities = World.GetPool<LuaStructuralCommandTests.Position>().ToSpan().ToArray();
            foreach (int entity in entities) World.DelEntity(entity);
            World.ReleaseDelEntityBufferAll();
        }
        public void Dispose() { Runtime.Dispose(); Preparer.Dispose(); Scope.Dispose(); World.Destroy(); }
    }
}
