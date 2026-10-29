using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;
using Karpik.Engine.Shared.ECS.Scheduling;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;

public sealed class MixedComponentQueryTests
{
    public struct Position : IEcsComponent { public float X, Y; }

    private sealed class Fixture : IDisposable
    {
        public EcsDefaultWorld World { get; } = new();
        public ComponentCallbackScope Scope { get; } = new();
        public ComponentQueryPreparer Preparer { get; }
        public Fixture()
        {
            var clr = new ClrComponentBindings([new GeneratedBindings()]); clr.Register<Position>("game:Position");
            var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze([new ComponentSchema("weather:Temperature", 1, new SchemaField("Value", SchemaScalarKind.F32))]);
            Preparer = new ComponentQueryPreparer(World, clr, schemas, Scope); Preparer.Initialize();
        }
        public int Entity(float y)
        {
            var positions = World.GetPool<Position>(); int entity = World.NewEntity(); positions.Add(entity).Y = y; return entity;
        }
        public void Dispose() { Scope.Dispose(); Preparer.Dispose(); World.Destroy(); }
    }

    private static ComponentQueryDefinition Pending(string name = "pending") => new(name,
        [new("game:Position")], [new("weather:Temperature", false)], []);

    [Fact]
    public void MixedComponentQuery_SelectsPositionWithoutTemperatureAndEnforcesRights()
    {
        using var f = new Fixture();
        var query = f.Preparer.Prepare(Pending(), "weather:init");
        int a = f.Entity(0), b = f.Entity(100), c = f.Entity(200);
        f.Scope.Enter("weather:init");
        try
        {
            var positions = query.Components["game:Position"]; var temperatures = query.Components["weather:Temperature"];
            temperatures.Add(c).WriteF32(0, 99);
            query.Capture(); var selected = ReadAll(query); Assert.Equal(new[] { a, b }.Order(), selected.Order());
            foreach (int entity in selected) temperatures.Add(entity).WriteF32(0, 20 - positions.Get(entity).ReadF32(1) * 0.01f);
            Assert.Equal(20, temperatures.Get(a).ReadF32(0)); Assert.Equal(19, temperatures.Get(b).ReadF32(0)); Assert.Equal(99, temperatures.Get(c).ReadF32(0));
            Assert.Throws<InvalidOperationException>(() => positions.Get(a).WriteF32(1, 3));
            Assert.Throws<InvalidOperationException>(() => positions.Add(a));
            query.Capture(); Assert.Empty(ReadAll(query));
        }
        finally { f.Scope.Exit(); }
    }

    [Fact]
    public void MixedComponentQuery_SnapshotNestedNamesAndSameNameRestart()
    {
        using var f = new Fixture();
        var first = f.Preparer.Prepare(Pending(), "owner"); var second = f.Preparer.Prepare(Pending("nested"), "owner");
        int a = f.Entity(1), b = f.Entity(2);
        f.Scope.Enter("owner");
        try
        {
            first.Capture(); Assert.True(first.MoveNext(out int visited));
            first.Components["weather:Temperature"].Add(visited);
            second.Capture(); Assert.Single(ReadAll(second));
            Assert.True(first.MoveNext(out int remaining)); Assert.NotEqual(visited, remaining);
            Assert.False(first.MoveNext(out _));
            first.Capture(); Assert.Single(ReadAll(first));
            first.Capture(); Assert.Single(ReadAll(first));
            Assert.Contains(remaining, new[] { a, b });
        }
        finally { f.Scope.Exit(); }
    }

    [Fact]
    public void MixedComponentQuery_GrowthPreservesActiveSnapshotAndView()
    {
        using var f = new Fixture(); var query = f.Preparer.Prepare(Pending(), "owner");
        int a = f.Entity(11), b = f.Entity(22); int capacity = f.World.Capacity;
        f.Scope.Enter("owner");
        try
        {
            query.Capture(); var view = query.Components["game:Position"].Get(a);
            for (int i = 0; i < capacity; i++) f.Entity(i);
            Assert.Equal(11, view.ReadF32(1));
            Assert.Equal(new[] { a, b }.Order(), ReadAll(query).Order());
        }
        finally { f.Scope.Exit(); }
    }

    [Fact]
    public void MixedComponentQuery_RejectsWrongOwnerAndInvalidIds()
    {
        using var f = new Fixture(); var query = f.Preparer.Prepare(Pending(), "owner"); int entity = f.Entity(3);
        var positions = query.Components["game:Position"];
        Assert.Throws<InvalidOperationException>(() => positions.Get(entity));
        f.Scope.Enter("owner"); var view = positions.Get(entity); f.Scope.Exit();
        f.Scope.Enter("other");
        Assert.Throws<InvalidOperationException>(() => view.ReadF32(1));
        f.Scope.Exit(); f.Scope.Enter("owner");
        try
        {
            Assert.Throws<InvalidOperationException>(() => positions.Has(-1));
            Assert.Throws<InvalidOperationException>(() => positions.Get(int.MaxValue));
            f.World.DelEntity(entity); Assert.Throws<InvalidOperationException>(() => view.ReadF32(1));
        }
        finally { f.Scope.Exit(); }
    }

    [Fact]
    public void MixedComponentQuery_OptionalUnknownTypesAndReservedCaptureAllocations()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidDataException>(() => f.Preparer.Prepare(new("bad", [], [], [new("missing")]), "owner"));
        var query = f.Preparer.Prepare(new("all", [], [], [new("weather:Temperature", false)]), "owner");
        int entity = f.Entity(1); f.Scope.Enter("owner");
        try
        {
            var handle = query.Components["weather:Temperature"]; handle.Add(entity);
            for (int i = 0; i < 2000; i++) { query.Capture(); while (query.MoveNext(out int id)) handle.Get(id).WriteF32(0, 7); }
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { query.Capture(); while (query.MoveNext(out int id)) handle.Get(id).WriteF32(0, 7); }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.Equal(0, bytes); Assert.Equal(7, handle.Get(entity).ReadF32(0));
        }
        finally { f.Scope.Exit(); }
    }

    private static int[] ReadAll(PreparedComponentQuery query)
    {
        var result = new List<int>(); while (query.MoveNext(out int entity)) result.Add(entity); return result.ToArray();
    }

    [Fact]
    public void MixedComponentQuery_DynamicIncAndSeparateHandlesPreserveRights()
    {
        using var f = new Fixture();
        var pending = f.Preparer.Prepare(Pending(), "owner");
        var both = f.Preparer.Prepare(new("both", [new("game:Position"), new("weather:Temperature")], [], []), "owner");
        var mutable = f.Preparer.Prepare(new("mutable", [], [], [new("game:Position", false)]), "owner");
        int entity = f.Entity(3); f.Entity(4); f.Entity(5);
        f.Scope.Enter("owner");
        try
        {
            pending.Components["weather:Temperature"].Add(entity);
            both.Capture(); Assert.Equal(new[] { entity }, ReadAll(both));
            var readOnly = pending.Components["game:Position"].Get(entity);
            mutable.Components["game:Position"].Get(entity).WriteF32(1, 9);
            Assert.Equal(9, readOnly.ReadF32(1));
            Assert.Throws<InvalidOperationException>(() => readOnly.WriteF32(1, 10));
        }
        finally { f.Scope.Exit(); }
    }

    [Fact]
    public void MixedComponentQuery_InferredAccessesIncludePresenceUnionAndAnchor()
    {
        using var f = new Fixture();
        var pending = f.Preparer.Prepare(Pending(), "owner");
        var mutable = f.Preparer.Prepare(new("mutable", [], [], [new("game:Position", false)]), "owner");
        var accesses = ComponentQueryPreparer.InferAccesses([pending, mutable]);
        Assert.Equal(3, accesses.Length);
        Assert.Contains(accesses, access => access.Key.ClrType == typeof(Position) && access.Mode == EcsAccessMode.Write);
        Assert.Contains(accesses, access => access.Key.DynamicId == "weather:Temperature" && access.Mode == EcsAccessMode.Write);
        Assert.Contains(accesses, access => access.Key.ClrType == typeof(DynamicComponentAnchor) && access.Mode == EcsAccessMode.Write);
        var readonlyQuery = f.Preparer.Prepare(new("excluded", [], [new("weather:Temperature")], []), "owner");
        var presence = Assert.Single(ComponentQueryPreparer.InferAccesses([readonlyQuery]));
        Assert.Equal("weather:Temperature", presence.Key.DynamicId); Assert.Equal(EcsAccessMode.Read, presence.Mode);
    }
}
