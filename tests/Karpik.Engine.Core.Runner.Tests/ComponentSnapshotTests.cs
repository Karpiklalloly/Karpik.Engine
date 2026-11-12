using System.Runtime.CompilerServices;
using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;
using GeneratedBindings = Karpik.Generated.Bindings_Karpik_002EEngine_002ECore_002ERunner_002ETests.GeneratedClrComponentBindingProvider;

[Collection("Lua ECS allocation probes")]
public sealed class ComponentSnapshotTests
{
    public struct ReadonlyData(int value) : IEcsComponent { public readonly int Value = value; }

    private sealed class Fixture : IDisposable
    {
        internal readonly EcsDefaultWorld World = new();
        internal readonly ComponentCallbackScope Scope = new();
        internal readonly ComponentQueryPreparer Preparer;
        internal Fixture()
        {
            var schemas = new ComponentSchemaRegistry();
            schemas.ValidateAndFreeze([Description("test:Data"), Description("test:Other")]);
            var bindings = new ClrComponentBindings([new GeneratedBindings()]);
            bindings.Register<ReadonlyData>("native:Readonly");
            Preparer = new(World, bindings, schemas, Scope); Preparer.Initialize();
        }
        internal PreparedComponentHandle Handle(string id = "test:Data", bool readOnly = false) =>
            Preparer.Prepare(new("snapshot", [], [], [new(id, readOnly)]), "snapshot").Components[id];
        public void Dispose() { Preparer.Dispose(); Scope.Dispose(); World.Destroy(); }
    }

    private static ComponentSchema Description(string id) => new(id, 1,
        new("Count", SchemaScalarKind.I32), new("Wide", SchemaScalarKind.I64),
        new("Position", SchemaValue.Struct(new SchemaField("X", SchemaScalarKind.F32))),
        new("Points", SchemaValue.Fixed(SchemaValue.Struct(new SchemaField("X", SchemaScalarKind.I32)), 2)));

    private static int Field(ReadOnlySpan<ComponentFieldDescriptor> fields, string name)
    {
        for (int i = 0; i < fields.Length; i++) if (fields[i].Name == name) return i;
        throw new InvalidOperationException(name);
    }

    [Fact]
    public void SnapshotCopiesEveryLeafAndIgnoresSubsequentSourceChanges()
    {
        using var f = new Fixture(); var source = f.Handle();
        int first = f.World.NewEntity(), second = f.World.NewEntity();
        f.Scope.Enter("snapshot");
        var view = source.Add(first);
        int count = Field(source.Fields.Span, "Count"), wide = Field(source.Fields.Span, "Wide");
        int x = Field(source.Fields.Span, "Position.X"), point = Field(source.Fields.Span, "Points[2].X");
        view.WriteI32(count, 7); view.WriteI64(wide, long.MaxValue); view.WriteF32(x, 2.5f); view.WriteI32(point, 99);
        var snapshot = source.Copy(first);
        var metadata = snapshot.Fields.ToArray();
        source.Add(second).WriteI32(count, 22);
        source.Get(first).WriteI32(count, 33);
        source.Del(first); f.World.ReleaseDelEntityBufferAll();
        f.Scope.Exit();
        Assert.Equal(7, snapshot.Read<int>(count)); Assert.Equal(long.MaxValue, snapshot.Read<long>(wide));
        Assert.Equal(2.5f, snapshot.Read<float>(x)); Assert.Equal(99, snapshot.Read<int>(point));
        snapshot.Write(count, 81); Assert.Equal(81, metadata[count].I32.Read(0));
        f.Scope.Enter("snapshot"); Assert.Equal(22, source.Get(second).ReadI32(count)); f.Scope.Exit();
    }

    [Fact]
    public void SnapshotBufferReusesMetadataAndRejectsIncompatibleSourcesBeforeMutation()
    {
        using var f = new Fixture(); using var otherSimulation = new Fixture();
        var source = f.Handle(); var otherComponent = f.Handle("test:Other"); var external = otherSimulation.Handle();
        int entity = f.World.NewEntity(), externalEntity = otherSimulation.World.NewEntity();
        f.Scope.Enter("snapshot"); otherSimulation.Scope.Enter("snapshot");
        int field = Field(source.Fields.Span, "Count");
        source.Add(entity).WriteI32(field, 17); otherComponent.Add(entity).WriteI32(field, 91);
        external.Add(externalEntity).WriteI32(field, 92);
        var buffer = source.NewSnapshot(); var metadata = buffer.Fields.ToArray();
        Assert.Equal(0, buffer.Read<int>(field));
        source.CopyTo(entity, buffer); Assert.Equal(17, buffer.Read<int>(field));
        Assert.Throws<InvalidOperationException>(() => otherComponent.CopyTo(entity, buffer));
        Assert.Throws<InvalidOperationException>(() => external.CopyTo(externalEntity, buffer));
        Assert.Equal(17, buffer.Read<int>(field));
        source.Get(entity).WriteI32(field, 18); source.CopyTo(entity, buffer);
        Assert.Equal(18, buffer.Read<int>(field));
        for (int i = 0; i < metadata.Length; i++) Assert.Same(metadata[i], buffer.Fields.Span[i]);
        f.Scope.Exit(); otherSimulation.Scope.Exit();
        Assert.Throws<InvalidOperationException>(() => source.CopyTo(entity, buffer));
    }

    [Fact]
    public void DetachedMutableDataDoesNotEscalateLiveReadonlyRights()
    {
        using var f = new Fixture(); var mutable = f.Handle(); var readOnly = f.Handle(readOnly: true);
        int entity = f.World.NewEntity(); f.Scope.Enter("snapshot");
        int field = Field(mutable.Fields.Span, "Count"); mutable.Add(entity).WriteI32(field, 6);
        var buffer = readOnly.Copy(entity); buffer.Write(field, 12);
        Assert.Equal(6, readOnly.Get(entity).ReadI32(field));
        Assert.Throws<InvalidOperationException>(() => readOnly.Get(entity).WriteI32(field, 14));
        f.Scope.Exit(); Assert.Equal(12, buffer.Read<int>(field));
    }

    [Fact]
    public void ClrReadonlyLeafIsPopulatedButCannotBeWrittenThroughAnySnapshotAccessor()
    {
        using var f = new Fixture(); var source = f.Handle("native:Readonly");
        int entity = f.World.NewEntity(); f.World.GetPool<ReadonlyData>().Add(entity) = new ReadonlyData(42);
        f.Scope.Enter("snapshot"); var snapshot = source.Copy(entity); f.Scope.Exit();
        Assert.Equal(42, snapshot.Read<int>(0));
        Assert.False(snapshot.Fields.Span[0].CanWrite);
        Assert.Throws<InvalidOperationException>(() => snapshot.Write(0, 10));
        Assert.Throws<InvalidOperationException>(() => snapshot.Fields.Span[0].I32.Write(0, 10));
        Assert.Throws<InvalidOperationException>(() => snapshot.Read<float>(0));
    }

    [Fact]
    public void RetainedSnapshotAndRawFieldAccessAreInvalidatedBySimulationDisposal()
    {
        var f = new Fixture(); var source = f.Handle(); int entity = f.World.NewEntity(); f.Scope.Enter("snapshot");
        source.Add(entity); var snapshot = source.Copy(entity); var raw = snapshot.Fields.Span[0].I32;
        f.Scope.Exit(); f.Dispose();
        Assert.Throws<ObjectDisposedException>(() => snapshot.ValidateAccess());
        Assert.Throws<ObjectDisposedException>(() => snapshot.Read<int>(0));
        Assert.Throws<ObjectDisposedException>(() => snapshot.Write(0, 1));
        Assert.Throws<ObjectDisposedException>(() => raw.Read(0));
        Assert.Throws<ObjectDisposedException>(() => raw.Write(0, 1));
    }

    [Fact]
    public void WarmedCopyToAllocatesZeroBytes()
    {
        using var f = new Fixture(); var source = f.Handle(); int entity = f.World.NewEntity(); f.Scope.Enter("snapshot");
        source.Add(entity); var buffer = source.NewSnapshot();
        for (int i = 0; i < 128; i++) source.CopyTo(entity, buffer);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) source.CopyTo(entity, buffer);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        f.Scope.Exit(); Assert.Equal(0, allocated);
    }

    [Fact]
    public void SnapshotPointerTokensKeepHostTypeAndRevocationChecks()
    {
        using var pointers = new ComponentPointerRegistry();
        var domain = pointers.CreateDomain();
        var pointerType = pointers.RegisterType("host:Int", typeof(int).MakePointerType(), domain);
        var otherType = pointers.RegisterType("host:UInt", typeof(uint).MakePointerType(), domain);
        var schemas = new ComponentSchemaRegistry();
        schemas.ValidateAndFreeze([new("test:Pointer", 1, new SchemaField("Address", SchemaValue.Pointer("host:Int")))]);
        var world = new EcsDefaultWorld(); using var scope = new ComponentCallbackScope();
        try
        {
            using var preparer = new ComponentQueryPreparer(world, new ClrComponentBindings([]), schemas, scope, pointers);
            preparer.Initialize();
            var source = preparer.Prepare(new("pointer", [], [], [new("test:Pointer", false)]), "snapshot").Components["test:Pointer"];
            int entity = world.NewEntity(); scope.Enter("snapshot");
            var token = pointers.Capture(pointerType, 42);
            source.Add(entity).WritePointer(0, token);
            var snapshot = source.Copy(entity); scope.Exit();
            Assert.Equal((nuint)42, pointers.ReadAddress(snapshot.ReadPointer(0), pointerType));
            Assert.Throws<InvalidOperationException>(() => snapshot.WritePointer(0, pointers.Capture(otherType, 43)));
            Assert.Equal((nuint)42, pointers.ReadAddress(snapshot.ReadPointer(0), pointerType));
            pointers.Revoke(domain);
            Assert.Throws<InvalidOperationException>(() => snapshot.ReadPointer(0));
            Assert.Throws<InvalidOperationException>(() => snapshot.Fields.Span[0].Pointer.Read(0));
            snapshot.WritePointer(0, default); Assert.True(snapshot.ReadPointer(0).IsNull);
        }
        finally { world.Destroy(); }
    }

    [Fact]
    public void SnapshotDoesNotRetainSourceWorldOrPool()
    {
        var result = CreateAndDisposeSource();
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(result.World.IsAlive); Assert.False(result.Pool.IsAlive);
        Assert.Throws<ObjectDisposedException>(() => result.Snapshot.ValidateAccess());
        GC.KeepAlive(result.Snapshot);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ComponentSnapshot Snapshot, WeakReference World, WeakReference Pool) CreateAndDisposeSource()
    {
        using var f = new Fixture(); var source = f.Handle(); int entity = f.World.NewEntity();
        f.Scope.Enter("snapshot"); source.Add(entity); var snapshot = source.Copy(entity); f.Scope.Exit();
        return (snapshot, new WeakReference(f.World), new WeakReference(source.Fields.Span[0].I32));
    }
}
