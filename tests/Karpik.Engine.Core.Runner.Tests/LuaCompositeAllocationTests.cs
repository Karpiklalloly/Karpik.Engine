using System.Diagnostics;
using Karpik.Engine.Shared.Modding.Bindings;
using MoonSharp.Interpreter;
using Xunit;
using Xunit.Abstractions;

[Collection("Lua ECS allocation probes")]
public sealed class LuaCompositeAllocationTests(ITestOutputHelper output)
{
    private const string Fields = """
        Position={kind='struct',fields={X='f32',Y='f64'}},
        Points={kind='fixed',element={kind='struct',fields={X='i32',Y='f64'}},length=2},Wide='i64'
        """;
    public static IEnumerable<object[]> Cases()
    {
        foreach (string scenario in new[] { "nested", "snapshot-copy-to", "pointer" })
            foreach (int count in new[] { 0, 1, 16, 256, 4096 }) yield return [scenario, count];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CanonicalCompositeLuaWorkloadFitsApprovedExperimentalBudget(string scenario, int count)
    {
        using var pointers = new ComponentPointerRegistry();
        ComponentPointerType pointerType = default;
        if (scenario == "pointer")
            pointerType = pointers.RegisterType("host:Int", typeof(int).MakePointerType(), pointers.CreateDomain());
        string fields = scenario == "pointer" ? "Pointer={kind='pointer',type='host:Int'}" : Fields;
        string body = scenario switch
        {
            "nested" => """
                local q=ctx.queries.q
                local source=q.components['test:Shape'];local target=q.components['test:Other']
                for entity in q:entities() do
                    local p=source:get(entity);local v=target:get(entity)
                    v.Position.X=p.Position.X;v.Position.Y=p.Position.Y
                    v.Points[1].X=p.Points[1].X;v.Points[1].Y=p.Points[1].Y
                    v.Points[2].X=p.Points[2].X;v.Points[2].Y=p.Points[2].Y
                    v.Wide=p.Wide+one
                end
                """,
            "snapshot-copy-to" => """
                local q=ctx.queries.q;local source=q.components['test:Shape']
                if not snapshotBuffer then snapshotBuffer=source:new_snapshot() end
                for entity in q:entities() do source:copy_to(entity,snapshotBuffer) end
                """,
            _ => """
                local q=ctx.queries.q
                local source=q.components['test:Shape'];local target=q.components['test:Other']
                for entity in q:entities() do target:get(entity).Pointer=source:get(entity).Pointer end
                """
        };
        using var f = new LuaCompositeFixture(body, fields, pointers: scenario == "pointer" ? pointers : null,
            preamble: "local one=types.i64('1')");
        var entities = new int[count];
        for (int i = 0; i < count; i++) entities[i] = f.Entity(withDestination: true);
        f.Inspect(() =>
        {
            foreach (int entity in entities)
            {
                var value = f.Source.Get(entity);
                if (scenario == "pointer") value.WritePointer(0, pointers.Capture(pointerType, 42));
                else
                {
                    value.Write(f.Field("Position.X"), 1f); value.Write(f.Field("Position.Y"), 2d);
                    value.Write(f.Field("Points[1].X"), 3); value.Write(f.Field("Points[1].Y"), 4d);
                    value.Write(f.Field("Points[2].X"), 5); value.Write(f.Field("Points[2].Y"), 6d);
                    value.Write(f.Field("Wide"), 7L);
                }
            }
        });
        var stats = Measure(f);
        int leaves = scenario == "pointer" ? 1 : 7;
        int exactOperations = scenario == "nested" ? 1 : 0;
        double ceiling = 4096d + 1600d * count * leaves + 544d * count * exactOperations;
        output.WriteLine($"Composite {scenario}; N={count}; L={leaves}; O={exactOperations}; samples=20; B/callback={stats.Bytes:F0}; ceiling={ceiling:F0}; p50_us={stats.P50:F3}; p95_us={stats.P95:F3}; p99_us={stats.P99:F3}; Gen0={stats.Gen0}; CLR={Environment.Version}");
        Assert.InRange(stats.Bytes, 0, ceiling);
        if (scenario == "snapshot-copy-to" && count > 0)
            Assert.Equal("7", f.Script.DoString("return tostring(snapshotBuffer.Wide)").String);
        else if (count > 0)
            f.Inspect(() =>
            {
                foreach (int entity in entities)
                {
                    var value = f.Destination.Get(entity);
                    if (scenario == "pointer") Assert.Equal((nuint)42, pointers.ReadAddress(value.ReadPointer(0), pointerType));
                    else
                    {
                        Assert.Equal(8L, value.Read<long>(f.Field("Wide")));
                        Assert.Equal(6d, value.Read<double>(f.Field("Points[2].Y")));
                    }
                }
            });
    }

    private static Stats Measure(LuaCompositeFixture fixture)
    {
        for (int i = 0; i < 16; i++) fixture.Runtime.InvokeSystem(0, 0.02);
        var ticks = new long[20]; long bytes = 0;
        int collections = GC.CollectionCount(0);
        for (int i = 0; i < ticks.Length; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
            fixture.Runtime.InvokeSystem(0, 0.02);
            ticks[i] = Stopwatch.GetTimestamp() - started;
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        collections = GC.CollectionCount(0) - collections;
        Array.Sort(ticks); double micros = 1_000_000d / Stopwatch.Frequency;
        return new(bytes / (double)ticks.Length, ticks[9] * micros, ticks[18] * micros, ticks[19] * micros, collections);
    }

    private readonly record struct Stats(double Bytes, double P50, double P95, double P99, int Gen0);
}
