using System.Diagnostics;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using Xunit;
using Xunit.Abstractions;

[CollectionDefinition("Lua ECS allocation probes", DisableParallelization = true)]
public sealed class LuaEcsAllocationCollection;

[Collection("Lua ECS allocation probes")]
public sealed class LuaEcsAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void PreparedView_IsReusedAndWritesThroughWithoutBoxedScalars()
    {
        var probe = new BoundaryProbe(2);
        DynValue check = probe.Script.DoString("""
            return function(dt, ctx)
                local first = ctx.temperatures:add(1)
                first.Value = 12.5
                local second = ctx.temperatures:add(2)
                second.Value = 7.25
                return first, second
            end
            """);
        DynValue result = probe.Call(check);

        // Lua copies DynValue wrappers; the reusable host view must remain the same object.
        Assert.Same(result.Tuple[0].UserData.Object, result.Tuple[1].UserData.Object);
        Assert.Same(probe.Temperatures.View, result.Tuple[0].UserData.Object);
        Assert.Equal(12.5f, probe.Temperatures.Values[1]);
        Assert.Equal(7.25f, probe.Temperatures.Values[2]);
        Assert.Equal(2, probe.Temperatures.View.EntityId);
        Assert.Equal(2, probe.Temperatures.AddCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(256)]
    [InlineData(4096)]
    public void BoundaryCosts_ReportAllocationsAndLatency(int count)
    {
        var probe = new BoundaryProbe(count);
        output.WriteLine($"MoonSharp={typeof(Script).Assembly.GetName().Version}; CLR={Environment.Version}; N={count}; warmup=2000; measured=1000");
        foreach ((string name, string body) in new[]
        {
            ("EmptyDt", "return function(dt) return 0 end"),
            ("EmptyDtAndContext", "return function(dt, ctx) return 0 end"),
            ("RawNumericLoop", $"return function(dt, ctx) local sum=0; for e=1,{count} do sum=sum+e end; return sum end"),
            ("PreparedIntIterator", "return function(dt, ctx) local sum=0; for e in ctx:entities() do sum=sum+e end; return sum end"),
            ("PreparedFloatRead", $"return function(dt, ctx) local sum=0; for e=1,{count} do local p=ctx.positions:get(e); sum=sum+p.Value end; return sum end"),
            ("PreparedFloatWrite", $"return function(dt, ctx) for e=1,{count} do local t=ctx.temperatures:get(e); t.Value=e end; return {count} end"),
            ("PreparedAdd", $"return function(dt, ctx) for e=1,{count} do ctx.temperatures:add(e) end; return {count} end"),
            ("PreparedAddGrowth", $"return function(dt, ctx) for e=1,{count} do ctx.temperatures:add(e) end; return {count} end"),
            ("PreparedFloatReadWrite", $"return function(dt, ctx) for e=1,{count} do local p=ctx.positions:get(e); local t=ctx.temperatures:get(e); t.Value=p.Value end; return {count} end"),
            ("FullSliceBoundary", $"return function(dt, ctx) for e in ctx:entities() do local p=ctx.positions:get(e); local t=ctx.temperatures:add(e); t.Value=20-p.Value*0.01 end; return {count} end")
        })
        {
            DynValue callback = probe.Script.DoString(body);
            probe.PassContext = name != "EmptyDt";
            probe.ForceGrowthOnReset = name == "PreparedAddGrowth";
            for (int warmup = 0; warmup < 2000; warmup++)
            {
                probe.Reset();
                probe.Call(callback);
            }

            const int iterations = 1000;
            var ticks = new long[iterations];
            long bytes = 0;
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            DynValue result = DynValue.Nil;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                // Restore add membership outside the measured callback; every sample does the same work.
                probe.Reset();
                long beginBytes = GC.GetAllocatedBytesForCurrentThread();
                long beginTicks = Stopwatch.GetTimestamp();
                result = probe.Call(callback);
                ticks[iteration] = Stopwatch.GetTimestamp() - beginTicks;
                bytes += GC.GetAllocatedBytesForCurrentThread() - beginBytes;
            }
            gen0 = GC.CollectionCount(0) - gen0;
            gen1 = GC.CollectionCount(1) - gen1;
            gen2 = GC.CollectionCount(2) - gen2;

            double expected = name is "RawNumericLoop" or "PreparedIntIterator" or "PreparedFloatRead"
                ? count * (count + 1d) / 2
                : name.StartsWith("Empty", StringComparison.Ordinal) ? 0 : count;
            Assert.Equal(expected, result.Number);
            if (name == "FullSliceBoundary")
                Assert.InRange(bytes / (double)iterations, 0, 4096d + 1600d * count);
            if (name == "PreparedIntIterator") Assert.Equal(count, probe.Visited);
            if (name is "PreparedAdd" or "PreparedAddGrowth" or "FullSliceBoundary") Assert.Equal(count, probe.Temperatures.AddCount);
            for (int entity = 1; entity <= count; entity++)
            {
                if (name == "PreparedFloatWrite" || name == "PreparedFloatReadWrite")
                    Assert.Equal((float)entity, probe.Temperatures.Values[entity]);
                if (name == "FullSliceBoundary")
                    Assert.Equal((float)(20 - entity * 0.01), probe.Temperatures.Values[entity]);
            }

            Array.Sort(ticks);
            double micros = 1_000_000d / Stopwatch.Frequency;
            output.WriteLine($"{name}: B/callback={bytes / (double)iterations:F0}; p50_us={ticks[499] * micros:F3}; p95_us={ticks[949] * micros:F3}; p99_us={ticks[989] * micros:F3}; GC={gen0}/{gen1}/{gen2}");
        }
    }

    // Test-only preallocated data and explicit MoonSharp descriptors isolate VM boundary costs.
    // They do not implement ECS storage, permissions, snapshot capture or production bindings.
    private sealed class BoundaryProbe
    {
        private readonly int _count;
        private readonly DynValue _iterator;
        private readonly DynValue[] _dt = new DynValue[1];
        private readonly DynValue[] _dtAndContext = new DynValue[2];
        public Script Script { get; } = new();
        public Pool Positions { get; }
        public Pool Temperatures { get; }
        public bool PassContext { get; set; } = true;
        public bool ForceGrowthOnReset { get; set; }
        public int Visited { get; private set; }

        public BoundaryProbe(int count)
        {
            _count = count;
            Positions = new Pool(count);
            Temperatures = new Pool(count);
            for (int entity = 1; entity <= count; entity++) Positions.Values[entity] = entity;
            _iterator = DynValue.NewCallback((_, _) => Visited < _count ? DynValue.NewNumber(++Visited) : DynValue.Nil);
            var context = new Table(Script);
            context.Set("entities", DynValue.NewCallback((_, _) =>
            {
                Visited = 0;
                return _iterator;
            }));
            context.Set("positions", UserData.Create(Positions, new PoolDescriptor()));
            context.Set("temperatures", UserData.Create(Temperatures, new PoolDescriptor()));
            _dtAndContext[1] = DynValue.NewTable(context);
        }

        public void Reset()
        {
            Visited = 0;
            Temperatures.AddCount = 0;
            if (ForceGrowthOnReset)
            {
                Temperatures.Present = new bool[1];
                Temperatures.Values = new float[1];
            }
            else
            {
                Array.Clear(Temperatures.Present);
                Array.Clear(Temperatures.Values);
            }
        }

        public DynValue Call(DynValue callback)
        {
            DynValue[] args = PassContext ? _dtAndContext : _dt;
            args[0] = DynValue.NewNumber(0.02);
            return Script.Call(callback, args);
        }
    }

    private sealed class Pool
    {
        public float[] Values;
        public bool[] Present;
        public readonly View View;
        public readonly DynValue Get;
        public readonly DynValue Add;
        public int AddCount;

        public Pool(int count)
        {
            Values = new float[count + 1];
            Present = new bool[count + 1];
            View = new View(this);
            DynValue viewValue = UserData.Create(View, new ViewDescriptor());
            Get = DynValue.NewCallback((_, args) =>
            {
                View.EntityId = checked((int)args[1].Number);
                return viewValue;
            });
            Add = DynValue.NewCallback((_, args) =>
            {
                int entity = checked((int)args[1].Number);
                if (entity >= Values.Length)
                {
                    int capacity = checked(Values.Length * 2);
                    Array.Resize(ref Values, capacity);
                    Array.Resize(ref Present, capacity);
                }
                if (Present[entity]) throw new ScriptRuntimeException("Duplicate probe add.");
                Present[entity] = true;
                Values[entity] = 0;
                AddCount++;
                View.EntityId = entity;
                return viewValue;
            });
        }
    }

    private sealed class View(Pool pool)
    {
        public Pool Pool { get; } = pool;
        public int EntityId;
    }

    private sealed class PoolDescriptor : IUserDataDescriptor
    {
        public string Name => "LuaEcsProbePool";
        public Type Type => typeof(Pool);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing) => index.String switch
        {
            "get" => ((Pool)obj).Get,
            "add" => ((Pool)obj).Add,
            _ => throw new ScriptRuntimeException("Unknown probe method.")
        };
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing) => false;
        public string AsString(object obj) => Name;
        public DynValue MetaIndex(Script script, object obj, string metaname) => null!;
        public bool IsTypeCompatible(Type type, object obj) => type.IsInstanceOfType(obj);
    }

    private sealed class ViewDescriptor : IUserDataDescriptor
    {
        public string Name => "LuaEcsProbeView";
        public Type Type => typeof(View);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            if (index.String != "Value") throw new ScriptRuntimeException("Unknown probe field.");
            var view = (View)obj;
            return DynValue.NewNumber(view.Pool.Values[view.EntityId]);
        }
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        {
            if (index.String != "Value") return false;
            var view = (View)obj;
            view.Pool.Values[view.EntityId] = (float)value.Number;
            return true;
        }
        public string AsString(object obj) => Name;
        public DynValue MetaIndex(Script script, object obj, string metaname) => null!;
        public bool IsTypeCompatible(Type type, object obj) => type.IsInstanceOfType(obj);
    }
}
