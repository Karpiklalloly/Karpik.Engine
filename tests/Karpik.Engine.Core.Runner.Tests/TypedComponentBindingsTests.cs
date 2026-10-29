using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;

public sealed class TypedComponentBindingsTests
{
    private static ComponentSchema Mixed() => new("game:Mixed", 1,
        new("U", SchemaScalarKind.U32), new("F", SchemaScalarKind.F32), new("B", SchemaScalarKind.Bool),
        new("I", SchemaScalarKind.I32), new("D", SchemaScalarKind.F64));

    private sealed class Fixture : IDisposable
    {
        internal readonly EcsDefaultWorld World = new();
        internal readonly ComponentCallbackScope Scope = new();
        internal readonly ComponentQueryPreparer Preparer;
        internal Fixture()
        {
            var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze([Mixed()]);
            Preparer = new(World, new ClrComponentBindings([]), schemas, Scope); Preparer.Initialize();
        }
        internal PreparedComponentHandle Handle(string owner = "owner", bool readOnly = false) =>
            Preparer.Prepare(new("mixed", [], [], [new("game:Mixed", readOnly)]), owner).Components["game:Mixed"];
        public void Dispose() { Preparer.Dispose(); Scope.Dispose(); World.Destroy(); }
    }

    [Fact]
    public void DynamicComponent_MixedKindsPreserveValuesAfterSwapGrowthAndReuse()
    {
        var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze([Mixed()]);
        var world = new EcsDefaultWorld();
        using var storage = new DynamicComponentStorage(world, schemas);
        try
        {
            var pool = storage.Resolve("game:Mixed"); var fields = pool.Fields.ToArray();
            Assert.Equal(new[] { "B", "D", "F", "I", "U" }, fields.Select(f => f.Name));
            var keep = world.GetPool<KeepAlive>();
            int a = world.NewEntity(); keep.Add(a); pool.Add(a);
            fields[0].Bool.Write(a, true); fields[1].F64.Write(a, 16777217d);
            fields[2].F32.Write(a, 1.25f); fields[3].I32.Write(a, int.MinValue); fields[4].U32.Write(a, uint.MaxValue);
            int last = 0;
            for (int i = 0; i < 4097; i++) { last = world.NewEntity(); pool.Add(last); }
            fields[1].F64.Write(last, 1d + Math.ScaleB(1d, -40));
            Assert.True(fields[0].Bool.Read(a)); Assert.Equal(16777217d, fields[1].F64.Read(a));
            Assert.Equal(1.25f, fields[2].F32.Read(a)); Assert.Equal(int.MinValue, fields[3].I32.Read(a));
            Assert.Equal(uint.MaxValue, fields[4].U32.Read(a));
            pool.Del(a); Assert.Equal(1d + Math.ScaleB(1d, -40), fields[1].F64.Read(last));
            pool.Add(a);
            Assert.False(fields[0].Bool.Read(a)); Assert.Equal(0d, fields[1].F64.Read(a));
            Assert.Equal(0f, fields[2].F32.Read(a)); Assert.Equal(0, fields[3].I32.Read(a)); Assert.Equal(0u, fields[4].U32.Read(a));
            fields[4].U32.Write(a, uint.MaxValue);
            world.DelEntity(a); Assert.Throws<InvalidOperationException>(() => fields[4].U32.Read(a));
            world.ReleaseDelEntityBufferAll(); int reused = world.NewEntity(); Assert.Equal(a, reused);
            pool.Add(reused); Assert.Equal(0u, fields[4].U32.Read(reused));
            storage.Dispose(); Assert.Throws<ObjectDisposedException>(() => fields[0].Bool.Read(reused));
        }
        finally { world.Destroy(); }
    }

    [Fact]
    public void DynamicComponent_SchemaKindChangeRejectedWithoutReplacingFrozenCatalog()
    {
        var schemas = new ComponentSchemaRegistry(); schemas.ValidateAndFreeze([Mixed()]);
        schemas.ValidateAndFreeze([Mixed() with { Fields = Mixed().Fields.Reverse().ToArray() }]);
        var changed = Mixed(); changed.Fields[0] = new("U", SchemaScalarKind.I32);
        Assert.Throws<InvalidDataException>(() => schemas.ValidateAndFreeze([changed]));
        Assert.Equal(SchemaScalarKind.U32, schemas.GetSchemas()[0].Fields.Single(f => f.Name == "U").Kind);
        Assert.Throws<InvalidDataException>(() => new ComponentSchemaRegistry().ValidateAndFreeze([new("bad", 1, new SchemaField("X", (SchemaScalarKind)999))]));
        Assert.Throws<InvalidDataException>(() => new ComponentSchemaRegistry().ValidateAndFreeze([new("bad", 1, new SchemaField("X", SchemaScalarKind.Bool), new("X", SchemaScalarKind.I32))]));
    }

    [Fact]
    public void MixedComponentQuery_TypedViewsEnforceKindRightsAndOwner()
    {
        using var f = new Fixture(); var handle = f.Handle(); var readOnly = f.Handle(readOnly: true);
        int entity = f.World.NewEntity(); f.Scope.Enter("owner");
        var view = handle.Add(entity); var ro = readOnly.Get(entity);
        view.WriteBool(0, true); view.WriteF64(1, 16777217d); view.WriteF32(2, 1.25f);
        view.WriteI32(3, int.MinValue); view.WriteU32(4, uint.MaxValue);
        Assert.Equal(SchemaScalarKind.Bool, view.GetFieldKind(0));
        Assert.Throws<InvalidOperationException>(() => view.ReadF32(0));
        Assert.Throws<InvalidOperationException>(() => view.WriteBool(3, false));
        Assert.Throws<InvalidOperationException>(() => ro.WriteBool(0, false));
        Assert.Throws<InvalidOperationException>(() => ro.WriteF64(1, 0));
        Assert.Throws<InvalidOperationException>(() => ro.WriteF32(2, 0));
        Assert.Throws<InvalidOperationException>(() => ro.WriteI32(3, 0));
        Assert.Throws<InvalidOperationException>(() => ro.WriteU32(4, 0));
        f.Scope.Exit(); Assert.Throws<InvalidOperationException>(() => view.GetFieldKind(0));
        f.Scope.Enter("other"); Assert.Throws<InvalidOperationException>(() => view.ReadBool(0)); f.Scope.Exit();
        f.Scope.Enter("owner"); f.Preparer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => view.ReadBool(0));
        Assert.Throws<ObjectDisposedException>(() => view.ReadF64(1));
        Assert.Throws<ObjectDisposedException>(() => view.ReadF32(2));
        Assert.Throws<ObjectDisposedException>(() => view.ReadI32(3));
        Assert.Throws<ObjectDisposedException>(() => view.ReadU32(4)); f.Scope.Exit();
    }

    [Fact]
    public void MixedComponentQuery_ReservedTypedAccessAllocatesZero()
    {
        using var f = new Fixture();
        var query = f.Preparer.Prepare(new("mixed", [new("game:Mixed", false)], [], []), "owner");
        var handle = query.Components["game:Mixed"]; int entity = f.World.NewEntity();
        f.Scope.Enter("owner"); handle.Add(entity);
        for (int i = 0; i < 2000; i++) Run();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Run();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, bytes); Assert.Equal(3000, handle.Get(entity).ReadI32(3)); f.Scope.Exit();
        void Run()
        {
            query.Capture();
            while (query.MoveNext(out int id))
            {
                var v = handle.Get(id); v.WriteBool(0, !v.ReadBool(0)); v.WriteF64(1, v.ReadF64(1) + 1);
                v.WriteF32(2, v.ReadF32(2) + 1); v.WriteI32(3, v.ReadI32(3) + 1); v.WriteU32(4, v.ReadU32(4) + 1);
            }
        }
    }

    [Fact]
    public void ComponentFieldDescriptor_F32CompatibilityAndReadonlyFields()
    {
        var field = new FloatField(); var descriptor = new ComponentFieldDescriptor(Name: "F", CanWrite: false, F32: field);
        var (name, canWrite, f32) = descriptor;
        Assert.Equal("F", name); Assert.False(canWrite); Assert.Same(field, f32); Assert.Same(field, descriptor.F32);
        Assert.Throws<ArgumentNullException>(() => new ComponentFieldDescriptor("F", true, (IComponentField<float>)null!));
        Assert.Throws<InvalidOperationException>(() => descriptor.Bool);
    }

    private sealed class FloatField : IComponentField<float>
    {
        public float Read(int entityId) => 0;
        public void Write(int entityId, float value) => throw new InvalidOperationException();
    }
    public struct KeepAlive : IEcsComponent;

    [Fact]
    public void ComponentFieldDescriptor_RecordFormattingDoesNotReadWrongKind()
    {
        var descriptor = new ComponentFieldDescriptor("F", true, new FloatField());
        Assert.Contains("F32", descriptor.ToString());
        Assert.Equal(descriptor, descriptor with { });
        Assert.Equal("Renamed", (descriptor with { Name = "Renamed" }).Name);
    }
}
