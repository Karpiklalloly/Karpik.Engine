using System.Diagnostics;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;

foreach (string workload in new[] { "flat", "dynamic-nested-fixed", "native-nested-fixed" })
foreach (int count in new[] { 0, 1, 16, 256, 4096 })
{
    var world = new EcsDefaultWorld(new EcsWorldConfig(entitiesCapacity: Math.Max(16, count + 1),
        poolComponentsCapacity: Math.Max(16, count + 1)));
    using var scope = new ComponentCallbackScope();
    var schemas = new ComponentSchemaRegistry();
    var bindings = workload == "native-nested-fixed" ? new ClrComponentBindings([new GeneratedBindings()]) : new ClrComponentBindings([]);
    string component = workload == "native-nested-fixed" ? "probe:Native" : "probe:Data";
    if (workload == "native-nested-fixed")
    {
        bindings.Register<NativeCompositeBindingsTests.NativeShape>(component);
        schemas.ValidateAndFreeze([]);
    }
    else
    {
        SchemaField[] fields = workload == "flat" ?
            [new("I", SchemaScalarKind.I32), new("D", SchemaScalarKind.F64)] :
            [new("Data", SchemaValue.Struct(new SchemaField("I", SchemaScalarKind.I32), new SchemaField("D", SchemaScalarKind.F64))),
             new("Points", SchemaValue.Fixed(SchemaValue.Struct(new SchemaField("X", SchemaScalarKind.F64)), 3))];
        schemas.ValidateAndFreeze([new ComponentSchema(component, 1, fields)]);
    }
    using var preparer = new ComponentQueryPreparer(world, bindings, schemas, scope);
    preparer.Initialize();
    var handle = preparer.Prepare(new("q", [], [], [new(component, false)]), "probe").Components[component];
    int leaves = 0, bytesPerEntity = 0;
    foreach (var field in handle.Fields.Span)
    {
        if (!field.CanWrite) continue;
        leaves++;
        bytesPerEntity += field.Kind switch
        {
            SchemaScalarKind.I32 or SchemaScalarKind.F32 => 4,
            SchemaScalarKind.F64 or SchemaScalarKind.I64 => 8,
            _ => throw new InvalidOperationException("Unsupported probe kind.")
        };
    }
    var commands = preparer.GetCommands(new()
    {
        MaxCommands = Math.Max(1, count * 2), MaxPendingEntities = count,
        MaxPayloadBytes = Math.Max(1, count * bytesPerEntity)
    });
    scope.Enter("probe");
    var snapshot = handle.NewSnapshot();
    for (int i = 0; i < snapshot.Fields.Length; i++)
    {
        if (!snapshot.Fields.Span[i].CanWrite) continue;
        switch (snapshot.GetFieldKind(i))
        {
            case SchemaScalarKind.I32: snapshot.WriteI32(i, 42); break;
            case SchemaScalarKind.F32: snapshot.WriteF32(i, 2.5f); break;
            case SchemaScalarKind.F64: snapshot.WriteF64(i, 3.25); break;
            case SchemaScalarKind.I64: snapshot.WriteI64(i, 9007199254740993L); break;
        }
    }
    int[] ids = new int[count];
    long firstBytes = GC.GetAllocatedBytesForCurrentThread(), firstTicks = Stopwatch.GetTimestamp();
    Cycle();
    firstTicks = Stopwatch.GetTimestamp() - firstTicks;
    firstBytes = GC.GetAllocatedBytesForCurrentThread() - firstBytes;
    VerifyAndReset();
    for (int i = 0; i < 32; i++) { Cycle(); VerifyAndReset(); }
    long bytes = 0;
    long[] ticks = new long[64];
    for (int i = 0; i < ticks.Length; i++)
    {
        long startBytes = GC.GetAllocatedBytesForCurrentThread(), startTicks = Stopwatch.GetTimestamp();
        Cycle();
        ticks[i] = Stopwatch.GetTimestamp() - startTicks;
        bytes += GC.GetAllocatedBytesForCurrentThread() - startBytes;
        VerifyAndReset();
    }
    Array.Sort(ticks);
    double micros = 1_000_000d / Stopwatch.Frequency;
    Console.WriteLine($"Common {workload} N={count};C={2 * count};V={leaves * count};cold_B={firstBytes};cold_us={firstTicks * micros:F3};steady_B/callback={bytes / (double)ticks.Length:F0};p50_us={ticks[31] * micros:F3};p95_us={ticks[60] * micros:F3};max_us={ticks[^1] * micros:F3};world_capacity={world.Capacity}");
    if (bytes != 0) throw new Exception("Common steady-state allocation gate failed.");
    scope.Exit(); preparer.Dispose(); world.Destroy();

    void Cycle()
    {
        commands.Begin("probe");
        for (int i = 0; i < count; i++)
        {
            var target = commands.Create(); commands.Add(handle, target, snapshot);
        }
        commands.Complete(); commands.End();
    }
    void VerifyAndReset()
    {
        var entities = world.Entities;
        if (entities.Count != count) throw new Exception("Incorrect created count.");
        for (int i = 0; i < count; i++) ids[i] = entities[i];
        // Inspect outside measured regions, including native readonly/default leaves.
        if (count > 0)
        {
            var view = handle.Get(ids[0]);
            for (int i = 0; i < handle.Fields.Length; i++)
            {
                var field = handle.Fields.Span[i];
                bool valid = field.Kind switch
                {
                    SchemaScalarKind.I32 => view.ReadI32(i) == (field.CanWrite ? 42 : 0),
                    SchemaScalarKind.F32 => view.ReadF32(i) == (field.CanWrite ? 2.5f : 0),
                    SchemaScalarKind.F64 => view.ReadF64(i) == (field.CanWrite ? 3.25 : 0),
                    SchemaScalarKind.I64 => view.ReadI64(i) == (field.CanWrite ? 9007199254740993L : 0),
                    _ => false
                };
                if (!valid) throw new Exception("Incorrect payload leaf: " + field.Name);
            }
        }
        for (int i = 0; i < count; i++) world.DelEntity(ids[i]);
        world.ReleaseDelEntityBufferAll();
    }
}
