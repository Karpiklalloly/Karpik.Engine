using System.Diagnostics;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;

public sealed partial class LuaTypedBindingsTests
{
    public static IEnumerable<object[]> ScalarAllocationCases()
    {
        foreach (var (field, token) in new[] { ("B", "bool"), ("I", "i32"), ("U", "u32"), ("D", "f64") })
            foreach (int count in new[] { 0, 1, 16, 256, 4096 }) yield return [field, token, count];
    }

    [Theory]
    [MemberData(nameof(ScalarAllocationCases))]
    public void LuaEcsAllocation_TypedSingleFieldStaysWithinApprovedBudget(string field, string token, int count)
    {
        using var f = new Fixture($"v.{field}=p.{field}", $"{field}='{token}'");
        int[] entities = Enumerable.Range(0, count).Select(_ => f.Entity(uniform: true)).ToArray();
        var stats = Measure(f, entities); Report(token, count, stats);
        Assert.InRange(stats.Bytes, 0, 4096d + 1600d * count);
        f.Inspect(() =>
        {
            foreach (int entity in entities)
            {
                var v = f.Values.Get(entity);
                switch (token)
                {
                    case "bool": Assert.True(v.ReadBool(0)); break;
                    case "i32": Assert.Equal(1, v.ReadI32(0)); break;
                    case "u32": Assert.Equal(1u, v.ReadU32(0)); break;
                    case "f64": Assert.Equal(1d, v.ReadF64(0)); break;
                }
            }
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(256)]
    [InlineData(4096)]
    public void LuaEcsAllocation_MixedFiveDoesNotExceedMatchedFiveFloatBaseline(int count)
    {
        using var mixed = new Fixture();
        using var baseline = new Fixture(fields: "B='f32',D='f32',F='f32',I='f32',U='f32'", allFloat: true);
        var mixedEntities = Enumerable.Range(0, count).Select(_ => mixed.Entity(uniform: true)).ToArray();
        var floatEntities = Enumerable.Range(0, count).Select(_ => baseline.Entity(uniform: true)).ToArray();
        var mixedStats = Measure(mixed, mixedEntities); var floatStats = Measure(baseline, floatEntities);
        Report("mixed-five", count, mixedStats); Report("five-f32", count, floatStats);
        Assert.True(mixedStats.Bytes <= floatStats.Bytes, $"Mixed {mixedStats.Bytes} B/callback exceeds matched float baseline {floatStats.Bytes} at N={count}.");
        mixed.Inspect(() =>
        {
            foreach (int entity in mixedEntities)
            {
                var v = mixed.Values.Get(entity); Assert.True(v.ReadBool(0));
                Assert.Equal(1d, v.ReadF64(1)); Assert.Equal(1f, v.ReadF32(2)); Assert.Equal(1, v.ReadI32(3)); Assert.Equal(1u, v.ReadU32(4));
            }
        });
        baseline.Inspect(() =>
        {
            foreach (int entity in floatEntities)
                for (int field = 0; field < 5; field++) Assert.Equal(1f, baseline.Values.Get(entity).ReadF32(field));
        });
    }

    [Fact]
    public void LuaEcsAllocation_ReportsFirstInvocationWithForcedColumnGrowth()
    {
        using var f = new Fixture();
        int capacity = f.World.Configs.TryGet<EcsWorldConfig>(out var config) ? config.PoolComponentsCapacity : EcsWorldConfig.Default.PoolComponentsCapacity;
        var entities = Enumerable.Range(0, capacity + 1).Select(_ => f.Entity(uniform: true)).ToArray();
        long start = GC.GetAllocatedBytesForCurrentThread(), time = Stopwatch.GetTimestamp();
        f.Runtime.InvokeSystem(0, 0.02);
        long elapsed = Stopwatch.GetTimestamp() - time, bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        output.WriteLine($"Forced growth first invocation N={entities.Length}; B/callback={bytes}; us={elapsed * 1_000_000d / Stopwatch.Frequency:F3}; includes query/VM cold costs");
        f.Inspect(() => { foreach (int entity in entities) Assert.Equal(1d, f.Values.Get(entity).ReadF64(1)); });
    }

    private static ProbeStats Measure(Fixture f, int[] entities)
    {
        for (int i = 0; i < 2000; i++) { f.Reset(entities); f.Runtime.InvokeSystem(0, 0.02); }
        var ticks = new long[1000]; long bytes = 0; int collections = GC.CollectionCount(0);
        for (int i = 0; i < ticks.Length; i++)
        {
            f.Reset(entities);
            long startBytes = GC.GetAllocatedBytesForCurrentThread(), startTicks = Stopwatch.GetTimestamp();
            f.Runtime.InvokeSystem(0, 0.02);
            ticks[i] = Stopwatch.GetTimestamp() - startTicks;
            bytes += GC.GetAllocatedBytesForCurrentThread() - startBytes;
        }
        collections = GC.CollectionCount(0) - collections;
        Array.Sort(ticks); double micros = 1_000_000d / Stopwatch.Frequency;
        return new(bytes / 1000d, ticks[499] * micros, ticks[949] * micros, ticks[989] * micros, collections);
    }

    private void Report(string scenario, int count, ProbeStats stats) =>
        output.WriteLine($"Typed ECS {scenario} N={count}; B/callback={stats.Bytes:F0}; p50_us={stats.P50:F3}; p95_us={stats.P95:F3}; p99_us={stats.P99:F3}; Gen0={stats.Gen0}; CLR={Environment.Version}");

    private readonly record struct ProbeStats(double Bytes, double P50, double P95, double P99, int Gen0);
}
